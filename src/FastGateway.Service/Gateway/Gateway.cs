﻿using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using FastGateway.Entities;
using FastGateway.Entities.Core;
using FastGateway.Service.BackgroundTask;
using FastGateway.Service.DataAccess;
using FastGateway.Service.Infrastructure;
using FastGateway.Service.Services;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace FastGateway.Service.Gateway;

/// <summary>
/// 网关服务
/// </summary>
public static class Gateway
{
    private static readonly ConcurrentDictionary<string, WebApplication> GatewayWebApplications = new();

    private const string Root = "Root";

    static readonly DestinationConfig StaticProxyDestination = new() { Address = "http://127.0.0.1" };

    /// <summary>
    /// 默认的内容类型提供程序
    /// </summary>
    private static readonly DefaultContentTypeProvider DefaultContentTypeProvider = new();

    /// <summary>
    /// 检查Server是否在线
    /// </summary>
    /// <param name="serverId"></param>
    /// <returns></returns>
    public static bool CheckServerOnline(string serverId)
    {
        return GatewayWebApplications.ContainsKey(serverId);
    }

    /// <summary>
    /// 是否存在80端口服务
    /// </summary>
    public static bool Has80Service { get; private set; }

    /// <summary>
    /// 关闭指定网关
    /// </summary>
    /// <param name="serverId"></param>
    /// <returns></returns>
    public static async Task<bool> CloseGateway(string serverId)
    {
        if (GatewayWebApplications.TryRemove(serverId, out var webApplication))
        {
            await webApplication.StopAsync();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 找到证书
    /// </summary>
    /// <param name="context"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    private static X509Certificate2 ServerCertificateSelector(ConnectionContext? context, string name)
    {
        try
        {
            var cert = CertService.GetCert(name);
            if (cert != null)
            {
                return new X509Certificate2(cert.Certs.File, cert.Certs.Password);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return new X509Certificate2(Path.Combine(AppContext.BaseDirectory, "gateway.pfx"), "010426");
    }

    /// <summary>
    /// 构建网关
    /// </summary>
    public static async Task BuilderGateway(Server server, List<DomainName> domainNames,
        List<BlacklistAndWhitelist> blacklistAndWhitelists, List<RateLimit> rateLimits)
    {
        try
        {
            if (!server.Enable)
            {
                return;
            }

            bool is80 = server.Listen == 80;
            if (is80)
            {
                Has80Service = true;
            }

            var builder = WebApplication.CreateBuilder();

            builder.WebHost.UseKestrel(options =>
            {
                if (server.IsHttps)
                {
                    if (is80)
                    {
                        options.Listen(IPAddress.Any, server.Listen);
                    }

                    options.Listen(IPAddress.Any, is80 ? 443 : server.Listen, listenOptions =>
                    {
                        Action<HttpsConnectionAdapterOptions> configure = adapterOptions =>
                        {
                            adapterOptions.ServerCertificateSelector = ServerCertificateSelector;
                        };

                        if (is80)
                        {
                            listenOptions.UseHttps(configure);
                        }

                        listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
                    });
                }
                else
                {
                    options.Listen(IPAddress.Any, server.Listen);
                }

                options.Limits.MaxRequestBodySize = null;
            });

            builder.Services.Configure<FormOptions>(options =>
            {
                options.ValueLengthLimit = int.MaxValue;
                options.MultipartBodyLengthLimit = long.MaxValue;
                options.MultipartHeadersLengthLimit = int.MaxValue;
            });

            var (routes, clusters) = BuildConfig(domainNames);

            builder.Services.AddRateLimitService(rateLimits);

            builder.Services
                .AddReverseProxy()
                .LoadFromMemory(routes, clusters)
                .AddGateway(server)
                .ConfigureHttpClient(((context, handler) =>
                {
                    handler.SslOptions.RemoteCertificateValidationCallback =
                        (sender, certificate, chain, errors) => true;
                }));

            var app = builder.Build();

            if (is80)
            {
                // 用于HTTPS证书签名校验
                app.Use(async (context, next) =>
                {
                    if (context.Request.Path.StartsWithSegments("/.well-known/acme-challenge", out var token))
                    {
                        await CertService.Challenge(context, token.Value![1..]);
                    }
                    else
                    {
                        await next.Invoke();
                    }
                });
            }

            app.UseInitGatewayMiddleware();

            app.Use((async (context, next) =>
            {
                // TODO: 暂时不记录GET请求|WebSocket请求
                if (context.Request.Method == "GET" || context.WebSockets.IsWebSocketRequest)
                {
                    await next(context);
                    return;
                }
                else
                {
                    var logger = context.RequestServices.GetRequiredService<ILogger<ApplicationLogger>>();
                    var sw = Stopwatch.StartNew();
                    var ip = context.Request.Headers["X-Forwarded-For"];
                    var applicationLogger = new ApplicationLogger()
                    {
                        Ip = ip,
                        Method = context.Request.Method,
                        Path = context.Request.Path,
                        Domain = context.Request.Host.Host,
                        UserAgent = context.Request.Headers.UserAgent.ToString(),
                        RequestTime = DateTime.Now,
                    };
                    string platform = "Unknown";
                    var userAgent = context.Request.Headers.UserAgent.ToString();
                    if (userAgent.Contains("Windows"))
                    {
                        platform = "Windows";
                    }
                    else if (userAgent.Contains("Linux"))
                    {
                        platform = "Linux";
                    }
                    else if (userAgent.Contains("Android") || userAgent.Contains("iPhone") ||
                             userAgent.Contains("iPad"))
                    {
                        platform = "Mobile";
                    }

                    try
                    {
                        await next(context);
                    }
                    catch (Exception e)
                    {
                        applicationLogger.Success = false;
                        applicationLogger.StatusCode = 500;
                        applicationLogger.Platform = platform;
                        applicationLogger.Elapsed = sw.ElapsedMilliseconds;
                        logger.LogError(e, "网关请求异常");
                        LoggerBackgroundTask.AddLogger(applicationLogger);
                        return;
                    }

                    sw.Stop();
                    applicationLogger.Platform = platform;
                    applicationLogger.Elapsed = sw.ElapsedMilliseconds;
                    applicationLogger.Success = context.Response.StatusCode == 200;
                    applicationLogger.StatusCode = context.Response.StatusCode;
                    LoggerBackgroundTask.AddLogger(applicationLogger);
                }
            }));

            app.UseRateLimitMiddleware(rateLimits);

            if (server.EnableBlacklist || server.EnableWhitelist)
            {
                app.UseBlacklistMiddleware(blacklistAndWhitelists);
            }

            GatewayWebApplications.TryAdd(server.Id, app);

            app.MapReverseProxy();

            app.Lifetime.ApplicationStopping.Register(() => { GatewayWebApplications.Remove(server.Id, out _); });

            await app.RunAsync();
        }
        finally
        {
            GatewayWebApplications.Remove(server.Id, out _);
        }
    }

    private static IReverseProxyBuilder AddGateway(this IReverseProxyBuilder builder, Server server)
    {
        builder.AddTransforms(context =>
        {
            var prefix = context.Route.Match.Path?
                .Replace("/{**catch-all}", "")
                .Replace("{**catch-all}", "");

            // 如果存在泛域名则需要保留原始Host
            if (context.Route.Match.Hosts?.Any(x => x.Contains('*')) == true)
            {
                context.AddOriginalHost(true);
            }

            if (!string.IsNullOrEmpty(prefix))
            {
                context.AddPathRemovePrefix(prefix);
            }

            #region 静态站点

            if (context.Route.Metadata!.TryGetValue(Root, out var root))
            {
                var tryFiles = context.Cluster!.Metadata!.Select(p => p.Key).ToArray();
                context.AddRequestTransform(async transformContext =>
                {
                    var response = transformContext.HttpContext.Response;
                    var path = Path.Combine(root, transformContext.Path.Value![1..]);

                    if (File.Exists(path))
                    {
                        DefaultContentTypeProvider.TryGetContentType(path, out var contentType);
                        response.Headers.ContentType = contentType;

                        await response.SendFileAsync(path);
                        return;
                    }

                    // 搜索 try_files
                    foreach (var tryFile in tryFiles)
                    {
                        var tryPath = Path.Combine(root, tryFile);

                        if (!File.Exists(tryPath)) continue;

                        DefaultContentTypeProvider.TryGetContentType(tryPath, out var contentType);
                        response.Headers.ContentType = contentType;

                        await response.SendFileAsync(tryPath);

                        return;
                    }

                    response.StatusCode = 404;
                });
            }

            #endregion

            if (server.CopyRequestHost)
            {
                context.AddRequestTransform(async transformContext =>
                {
                    // 获取请求的host
                    var host = transformContext.HttpContext.Request.Host.Host;

                    // 设置到请求头
                    transformContext.ProxyRequest.Headers.Host = host;

                    await Task.CompletedTask.ConfigureAwait(false);
                });
            }
        });

        return builder;
    }

    private static WebApplication UseInitGatewayMiddleware(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            // 设置ip
            var ip = context.Connection.RemoteIpAddress;
            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
            {
                ip = IPAddress.Parse(forwardedFor);
            }
            else
            {
                context.Request.Headers["X-Forwarded-For"] = ip.ToString();
            }

            await next(context);
        });

        return app;
    }

    private static (IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters) BuildConfig(List<DomainName> domainNames)
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();


        foreach (var domainName in domainNames)
        {
            if (string.IsNullOrWhiteSpace(domainName.Path))
            {
                domainName.Path = "/{**catch-all}";
            }
            else if (domainName.Path == "/")
            {
                domainName.Path = $"/{{**catch-all}}";
            }
            else
            {
                domainName.Path = $"/{domainName.Path}/{{**catch-all}}";
            }


            Dictionary<string, string> routeMetadata, clusterMetadata;

            if (domainName.ServiceType == ServiceType.StaticFile)
            {
                routeMetadata = new Dictionary<string, string>(1) { { Root, domainName.Root! } };

                clusterMetadata = new Dictionary<string, string>(domainName.TryFiles!.Length);
                foreach (var item in domainName.TryFiles)
                {
                    clusterMetadata.Add(item, string.Empty);
                }
            }
            else
            {
                routeMetadata = clusterMetadata = new Dictionary<string, string>(0);
            }

            RouteConfig route = new RouteConfig
            {
                RouteId = domainName.Id,
                ClusterId = domainName.Id,
                Match = new RouteMatch
                {
                    Hosts = domainName.Domains,
                    Path = domainName.Path,
                },
                Metadata = routeMetadata
            };

            if (domainName.ServiceType == ServiceType.Service)
            {
                var cluster = new ClusterConfig
                {
                    ClusterId = domainName.Id,
                    Destinations = new Dictionary<string, DestinationConfig>()
                    {
                        {
                            Guid.NewGuid().ToString("N"),
                            new DestinationConfig()
                            {
                                Address = domainName.Service,
                            }
                        }
                    }
                };

                clusters.Add(cluster);
            }

            if (domainName.ServiceType == ServiceType.ServiceCluster)
            {
                if (string.IsNullOrWhiteSpace(domainName.Path))
                {
                    domainName.Path = "/{**catch-all}";
                }
                else if (domainName.Path == "/")
                {
                    domainName.Path = $"/{{**catch-all}}";
                }
                else
                {
                    domainName.Path = $"/{domainName.Path}/{{**catch-all}}";
                }

                if (domainName.ServiceType == ServiceType.Service)
                {
                    var destinations = domainName.UpStreams.Select(x => new DestinationConfig
                    {
                        Address = x.Service
                    }).ToDictionary(x => Guid.NewGuid().ToString("N"));

                    var cluster = new ClusterConfig
                    {
                        ClusterId = domainName.Id,
                        Destinations = destinations
                    };

                    clusters.Add(cluster);
                }
            }

            if (domainName.ServiceType == ServiceType.StaticFile)
            {
                var cluster = new ClusterConfig
                {
                    ClusterId = domainName.Id,
                    Destinations = new Dictionary<string, DestinationConfig>()
                    {
                        {
                            Guid.NewGuid().ToString("N"),
                            StaticProxyDestination
                        }
                    },
                    Metadata = clusterMetadata
                };

                clusters.Add(cluster);
            }

            routes.Add(route);
        }

        return (routes, clusters);
    }
}