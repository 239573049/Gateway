﻿using AspNetCoreRateLimit;
using FastGateway.Infrastructures;
using FastGateway.Middlewares;
using FastGateway.TunnelServer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Directory = System.IO.Directory;

namespace FastGateway.Services;

public static class ApiServiceService
{
    private static readonly Dictionary<string, WebApplication> WebApplications = new();

    /// <summary>
    /// 默认的内容类型提供程序
    /// </summary>
    private static readonly DefaultContentTypeProvider DefaultContentTypeProvider = new();

    /// <summary>
    /// 客户端连接
    /// </summary>
    private static readonly ConcurrentDictionary<string, List<string>> ClientConnections = new();

    /// <summary>
    /// 是否存在80端口服务
    /// </summary>
    public static bool HasHttpService { get; private set; }

    public static async Task LoadServices(IFreeSql freeSql)
    {
        // 停止所有服务
        foreach (var application in WebApplications)
        {
            await application.Value.DisposeAsync();
        }

        WebApplications.Clear();

        var services = await freeSql.Select<Service>()
            .IncludeMany(x => x.Locations)
            .ToListAsync();

        var rateLimitIds = services.Select(x => x.RateLimitName).Distinct().ToArray();

        var rateLimits = await freeSql.Select<RateLimit>()
            .Where(x => rateLimitIds.Contains(x.Name))
            .ToListAsync();

        foreach (var service in services)
        {
            service.RateLimit = rateLimits.FirstOrDefault(x => x.Name == service.RateLimitName);
            await Task.Factory.StartNew(BuilderService, service);
        }
    }

    [Authorize]
    public static async Task CreateAsync(ServiceInput input, IFreeSql freeSql)
    {
        if (await freeSql.Select<Service>().Where(x => x.Listen == input.Listen).CountAsync() > 0)
        {
            throw new Exception("端口已被占用");
        }

        // 可能是更新
        if (!string.IsNullOrEmpty(input.Id) && await freeSql.Select<Service>().AnyAsync(x => x.Id == input.Id))
        {
            await UpdateAsync(input.Id, input, freeSql);
            return;
        }

        var serviceId = Guid.NewGuid().ToString("N");
        var service = new Service()
        {
            Id = serviceId,
            Enable = input.Enable,
            RedirectHttps = input.RedirectHttps,
            EnableFlowMonitoring = input.EnableFlowMonitoring,
            EnableTunnel = input.EnableTunnel,
            EnableWhitelist = input.EnableWhitelist,
            RateLimitName = input.RateLimitName,
            EnableBlacklist = input.EnableBlacklist,
            IsHttps = input.IsHttps,
            Listen = input.Listen,
            Locations = input.Locations.Select(x => new Location()
            {
                ServiceId = serviceId,
                Id = Guid.NewGuid().ToString("N"),
                ServiceNames = x.ServiceNames,
                LocationService = x.LocationService.Select(x => new LocationService()
                {
                    AddHeader = x.AddHeader,
                    Path = x.Path,
                    ProxyPass = x.ProxyPass,
                    Root = x.Root,
                    Type = x.Type,
                    TryFiles = x.TryFiles,
                    LoadType = x.LoadType,
                    UpStreams = x.UpStreams.Select(x => new UpStream()
                    {
                        Server = x.Server,
                        Weight = x.Weight
                    }).ToList()
                }).ToList()
            }).ToList()
        };

        await freeSql.Insert(service).ExecuteAffrowsAsync();

        await freeSql.Insert(service.Locations).ExecuteAffrowsAsync();
    }

    [Authorize]
    public static async Task UpdateAsync(string id, ServiceInput input, IFreeSql freeSql)
    {
        await freeSql.Update<Service>()
            .Where(x => x.Id == id)
            .Set(x => x.Listen, input.Listen)
            .Set(x => x.IsHttps, input.IsHttps)
            .Set(x => x.EnableBlacklist, input.EnableBlacklist)
            .Set(x => x.EnableWhitelist, input.EnableWhitelist)
            .Set(x => x.Enable, input.Enable)
            .Set(x => x.EnableTunnel, input.EnableTunnel)
            .Set(x => x.RedirectHttps, input.RedirectHttps)
            .Set(x => x.RateLimitName, input.RateLimitName)
            .Set(x => x.EnableFlowMonitoring, input.EnableFlowMonitoring)
            .ExecuteAffrowsAsync();

        await freeSql.Delete<Location>()
            .Where(x => x.ServiceId == id)
            .ExecuteAffrowsAsync();

        await freeSql.Insert(input.Locations.Select(x => new Location()
        {
            ServiceNames = x.ServiceNames.Where(x => !string.IsNullOrEmpty(x)).ToArray(),
            Id = Guid.NewGuid().ToString("N"),
            ServiceId = id,
            LocationService = x.LocationService.Select(x => new LocationService()
            {
                AddHeader = x.AddHeader,
                Path = x.Path,
                ProxyPass = x.ProxyPass,
                Root = x.Root,
                Type = x.Type,
                TryFiles = x.TryFiles,
                UpStreams = x.UpStreams.Select(x => new UpStream()
                {
                    Server = x.Server,
                    Weight = x.Weight
                }).ToList(),
                LoadType = x.LoadType,
            }).ToList()
        })).ExecuteAffrowsAsync();
    }

    [Authorize]
    public static async Task DeleteAsync(string id, IFreeSql freeSql)
    {
        if (WebApplications.Remove(id, out var app))
        {
            await app.DisposeAsync();
        }

        await freeSql.Delete<Service>()
            .Where(x => x.Id == id)
            .ExecuteAffrowsAsync();

        await freeSql.Delete<Location>()
            .Where(x => x.ServiceId == id)
            .ExecuteAffrowsAsync();
    }

    /// <summary>
    /// 校验目录是否存在
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static ResultDto<bool> CheckDirectoryExistenceAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ResultDto<bool>.ErrorResult("Path is empty");
        }

        // 判断path是文件还是目录
        var isDirectory = path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar);

        if (isDirectory)
        {
            return !Directory.Exists(Path.GetDirectoryName(path))
                ? ResultDto<bool>.ErrorResult($"未找到：{path}")
                : ResultDto<bool>.SuccessResult(true);
        }
        else
        {
            return !Directory.Exists(path)
                ? ResultDto<bool>.ErrorResult($"未找到：{path}")
                : ResultDto<bool>.SuccessResult(true);
        }
    }

    [Authorize]
    public static async Task<Service?> GetAsync(string id, IFreeSql freeSql)
    {
        var service = await freeSql.Select<Service>()
            .IncludeMany(x => x.Locations)
            .Where(x => x.Id == id)
            .FirstAsync();

        if (service == null)
            return default;

        service.RateLimit = await freeSql.Select<RateLimit>()
            .Where(x => x.Name == service.RateLimitName)
            .FirstAsync();

        return service;
    }

    [Authorize]
    public static async Task<ResultDto<PageResultDto<Service>>> GetListAsync(int page, int pageSize,
        IFreeSql freeSql)
    {
        if (page < 1)
        {
            page = 1;
        }

        pageSize = pageSize switch
        {
            < 1 => 10,
            > 100 => 100,
            _ => pageSize
        };

        var result = await freeSql.Select<Service>()
            .IncludeMany(x => x.Locations)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var total = await freeSql.Select<Service>().CountAsync();

        return ResultDto<PageResultDto<Service>>.SuccessResult(new PageResultDto<Service>(result, total));
    }

    [Authorize]
    public static ResultDto<Dictionary<string, bool>> ServiceStats([FromBody] List<string> ids)
    {
        var result = new Dictionary<string, bool>();

        foreach (var id in ids)
        {
            if (WebApplications.TryGetValue(id, out var app))
            {
                result.Add(id, true);
            }
        }

        return ResultDto<Dictionary<string, bool>>.SuccessResult(result);
    }

    [Authorize]
    public static async Task StartServiceAsync(string id, IFreeSql freeSql)
    {
        if (WebApplications.TryGetValue(id, out var app))
        {
            await app.StartAsync();
        }
        else
        {
            var service = await GetAsync(id, freeSql);

            await Task.Factory.StartNew(BuilderService, service);

            await Task.Delay(500);
        }
    }

    [Authorize]
    public static async Task StopServiceAsync(string id)
    {
        if (WebApplications.TryGetValue(id, out var app))
        {
            await app.StopAsync();
            WebApplications.Remove(id);
        }
    }

    [Authorize]
    public static async Task RestartServiceAsync(string id, IFreeSql freeSql)
    {
        if (WebApplications.TryGetValue(id, out var app))
        {
            await app.StopAsync();

            await app.DisposeAsync();

            WebApplications.Remove(id, out _);

            var service = await GetAsync(id, freeSql);

            await Task.Factory.StartNew(BuilderService, service);

            await Task.Delay(500);
        }
    }

    [Authorize]
    public static async Task RestartConfigAsync(string id, IFreeSql freeSql)
    {
        var service = await GetAsync(id, freeSql);

        if (service == null)
        {
            return;
        }

        if (WebApplications.TryGetValue(id, out var app))
        {
            var (routes, clusters) = BuilderGateway(service);

            var memoryConfigProvider = app.Services.GetRequiredService<InMemoryConfigProvider>();

            memoryConfigProvider.Update(routes, clusters);
        }
    }

    [Authorize]
    public static List<string> ClientConnect(string serviceId)
    {
        if (ClientConnections.TryGetValue(serviceId, out var list))
        {
            return list;
        }

        return new()
        {
        };
    }

    private static async Task BuilderService(object state)
    {
        var service = (Service)state;
        try
        {
            // 使用最小的配置
            var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());

            if (service.Listen == 80)
            {
                HasHttpService = true;
            }

            builder.WebHost.UseKestrel(options =>
            {
                options.Listen(IPAddress.Any, service.Listen);

                if (HasHttpService && service.IsHttps)
                {
                    options.Listen(IPAddress.Any, 443, listenOptions =>
                    {
                        listenOptions.UseHttps(adapterOptions =>
                        {
                            adapterOptions.ServerCertificateSelector = (context, name) =>
                                CertService.Certs.TryGetValue(name, out var cert)
                                    ? new X509Certificate2(cert.File, cert.Password)
                                    : new X509Certificate2(Path.Combine(AppContext.BaseDirectory, "gateway.pfx"), "010426");
                        });

                        listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
                    });
                }

                options.Limits.MaxRequestBodySize = null;
            });

            builder.Services.Configure<FormOptions>(options =>
            {
                options.ValueLengthLimit = int.MaxValue;
                options.MultipartBodyLengthLimit = long.MaxValue;
                options.MultipartHeadersLengthLimit = int.MaxValue;
            });

            var (routes, clusters) = BuilderGateway(service);

            builder.Services.AddSingleton<ICurrentContext>(new CurrentContext()
            {
                ServiceId = service.Id,
            }).AddSingleton<StatisticsMiddleware>();

            // 如果启用限流则添加限流中间件
            if (service.RateLimit is { Enable: true })
            {
                builder.Services.AddMemoryCache();
                builder.Services.Configure<IpRateLimitOptions>
                (options =>
                {
                    options.GeneralRules = service.RateLimit.GeneralRules.Select(x => new RateLimitRule()
                    {
                        Endpoint = x.Endpoint,
                        Period = x.Period,
                        Limit = x.Limit,
                    }).ToList();

                    options.ClientWhitelist = service.RateLimit.ClientWhitelist;
                    options.ClientIdHeader = service.RateLimit.ClientIdHeader;
                    options.DisableRateLimitHeaders = service.RateLimit.DisableRateLimitHeaders;
                    options.EnableEndpointRateLimiting = service.RateLimit.EnableEndpointRateLimiting;
                    options.EnableRegexRuleMatching = service.RateLimit.EnableRegexRuleMatching;
                    options.EndpointWhitelist = service.RateLimit.EndpointWhitelist;
                    options.IpWhitelist = service.RateLimit.IpWhitelist;
                    options.RealIpHeader = service.RateLimit.RealIpHeader;
                    options.RateLimitCounterPrefix = service.RateLimit.RateLimitCounterPrefix;
                    options.RequestBlockedBehaviorAsync = async (context, _, _, _) =>
                    {
                        context.Response.StatusCode = service.RateLimit.HttpStatusCode;
                        context.Response.ContentType = service.RateLimit.RateLimitContentType;
                        await context.Response.WriteAsync(service.RateLimit.QuotaExceededMessage);
                    };
                });
                builder.Services.AddSingleton<IRateLimitConfiguration,
                    RateLimitConfiguration>();
                builder.Services.AddInMemoryRateLimiting();
            }


            builder.Services.AddReverseProxy()
                .LoadFromMemory(routes, clusters)
                // 删除所有代理的前缀
                .AddTransforms(context =>
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
                });

            if (service.EnableTunnel)
            {
                builder.Services.AddTunnelServices();
            }

            var app = builder.Build();

            if (service.RedirectHttps)
            {
                app.UseHttpsRedirection();
            }

            if (service.RateLimit is { Enable: true })
            {
                app.Use((async (context, next) =>
                {
                    // 获取ip
                    var ip = context.Connection.RemoteIpAddress?.MapToIPv4()?.ToString();

                    // 如果请求头中包含X-Forwarded-For则使用X-Forwarded-For
                    if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
                    {
                        ip = forwardedFor;
                    }
                    else
                    {
                        context.Request.Headers["X-Forwarded-For"] = ip;
                    }

                    await next(context);
                }));

                app.UseIpRateLimiting();
            }

            WebApplications.Add(service.Id, app);

            // 如果启用白名单则添加中间件
            if (service.EnableWhitelist)
            {
                app.Use(async (context, next) =>
                {
                    // 获取当前请求的IP
                    var ip = string.Empty;
                    if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
                    {
                        ip = forwardedFor;
                    }

                    if (ProtectionService.CheckBlacklistAndWhitelist(ip, ProtectionType.Whitelist))
                    {
                        context.Response.StatusCode = 403;
                        return;
                    }

                    await next(context);
                });
            }
            else if (service.EnableBlacklist)
            {
                app.Use(async (context, next) =>
                {
                    // 获取当前请求的IP
                    var ip = string.Empty;
                    if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
                    {
                        ip = forwardedFor;
                    }

                    if (ProtectionService.CheckBlacklistAndWhitelist(ip, ProtectionType.Blacklist))
                    {
                        context.Response.StatusCode = 403;
                        return;
                    }

                    await next(context);
                });
            }

            app.UseMiddleware<StatisticsMiddleware>();

            if (service.EnableTunnel)
            {
                // app.MapWebSocketTunnel("/gateway/connect-ws");

                // Auth可以添加到这个端点，我们可以将它限制在某些点上
                // 避免外部流量撞击它
                app.MapHttp2Tunnel("/gateway/connect-h2", connection =>
                {
                    ClientConnections.AddOrUpdate(service.Id, [connection], (s, list) =>
                    {
                        list.Add(connection);
                        return list;
                    });
                }, disconnection =>
                {
                    // 移除连接
                    if (ClientConnections.TryGetValue(service.Id, out var list))
                    {
                        list.Remove(disconnection);
                    }
                });
            }

            // 用于HTTPS证书签名校验
            app.MapGet("/.well-known/acme-challenge/{token}", AcmeChallenge.Challenge);

            foreach (var location in service.Locations.SelectMany(x => x.LocationService)
                         .Where(x => x.Type == ApiServiceType.StaticProxy))
            {
                app.Map(location.Path.TrimEnd('/'), app =>
                {
                    app.Run((async context =>
                    {
                        var path = Path.Combine(location.Root, context.Request.Path.Value[1..]);

                        if (File.Exists(path))
                        {
                            DefaultContentTypeProvider.TryGetContentType(path, out var contentType);
                            context.Response.Headers.ContentType = contentType;

                            await context.Response.SendFileAsync(path);

                            return;
                        }

                        if (location.TryFiles == null || location.TryFiles.Length == 0)
                        {
                            context.Response.StatusCode = 404;
                            return;
                        }

                        // 搜索 try_files
                        foreach (var tryFile in location.TryFiles)
                        {
                            var tryPath = Path.Combine(location.Root, tryFile);

                            if (!File.Exists(tryPath)) continue;

                            DefaultContentTypeProvider.TryGetContentType(tryPath, out var contentType);
                            context.Response.Headers.ContentType = contentType;

                            await context.Response.SendFileAsync(tryPath);

                            return;
                        }
                    }));
                });
            }

            app.MapReverseProxy();

            await app.RunAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        finally
        {
            if (service.Listen == 80)
            {
                HasHttpService = false;
            }
        }
    }

    private static (List<RouteConfig>, List<ClusterConfig>) BuilderGateway(Service service)
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();
        // 绑定路由到service
        foreach (var location in service.Locations)
        {
            var clusterId = location.Id;
            var destinations = new Dictionary<string, DestinationConfig>();

            var cluster = new ClusterConfig()
            {
                Destinations = destinations,
                ClusterId = clusterId,
            };
            clusters.Add(cluster);
            foreach (var locationService in location.LocationService)
            {
                var route = new RouteConfig()
                {
                    RouteId = Guid.NewGuid().ToString("N"),
                    ClusterId = clusterId,
                    Match = new RouteMatch()
                    {
                        Path = locationService.Path.TrimEnd('/') + "/{**catch-all}",
                        Hosts = location.ServiceNames
                    }
                };
                routes.Add(route);


                if (locationService.Type == ApiServiceType.SingleService)
                {
                    destinations.Add(Guid.NewGuid().ToString("N"), new DestinationConfig()
                    {
                        Address = locationService.ProxyPass,
                        Host = new Uri(locationService.ProxyPass).Host,
                    });
                    continue;
                }

                if (locationService.Type == ApiServiceType.LoadBalance)
                {
                    foreach (var upStream in locationService.UpStreams.Where(x => !string.IsNullOrEmpty(x.Server)))
                    {
                        destinations.Add(upStream.Server!, new DestinationConfig()
                        {
                            Address = upStream.Server!,
                        });
                    }
                }
                else if (locationService.Type == ApiServiceType.StaticProxy)
                {
                    routes.Remove(route);
                    clusters.Remove(cluster);
                }
            }
        }

        return (routes, clusters);
    }
}