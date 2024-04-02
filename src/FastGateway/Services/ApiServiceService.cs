﻿using System.Net;
using System.Security.Cryptography.X509Certificates;
using FastGateway.Infrastructures;
using FastGateway.Middlewares;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace FastGateway.Services;

public class ApiServiceService
{
    private static readonly Dictionary<string, WebApplication> WebApplications = new();

    public static async Task LoadServices(MasterDbContext masterDbContext)
    {
        foreach (var application in WebApplications)
        {
            await application.Value.DisposeAsync();
        }

        WebApplications.Clear();

        var services = await masterDbContext.Services
            .AsNoTracking()
            .Include(x => x.Locations)
            .ToListAsync();

        foreach (var service in services)
        {
            await Task.Factory.StartNew(BuilderService, service);
        }
    }

    [Authorize]
    public static async Task CreateAsync(ServiceInput input, MasterDbContext masterDbContext)
    {
        var service = input.Adapt<Service>();

        service.Id = Guid.NewGuid().ToString();

        service.Locations.ForEach(x =>
        {
            x.Id = Guid.NewGuid().ToString();

            x.ServiceId = service.Id;
        });

        await masterDbContext.Services.AddAsync(service);

        await masterDbContext.SaveChangesAsync();

        await Task.Factory.StartNew(BuilderService, service);
    }

    [Authorize]
    public static async Task UpdateAsync(string id, ServiceInput input, MasterDbContext masterDbContext)
    {
        var service = await masterDbContext.Services.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);

        if (service == null)
        {
            throw new Exception("Service not found");
        }

        input.Adapt(service);

        masterDbContext.Update(service);

        await masterDbContext.SaveChangesAsync();
    }

    [Authorize]
    public static async Task DeleteAsync(string id, MasterDbContext masterDbContext)
    {
        try
        {
            await masterDbContext.Database.BeginTransactionAsync();

            if (WebApplications.Remove(id, out var app))
            {
                await app.DisposeAsync();
            }


            await masterDbContext.Services.Where(x => x.Id == id)
                .ExecuteDeleteAsync();

            await masterDbContext.Locations.Where(x => x.ServiceId == id)
                .ExecuteDeleteAsync();

            await masterDbContext.Database.CommitTransactionAsync();
        }
        catch (Exception e)
        {
            await masterDbContext.Database.RollbackTransactionAsync();

            throw e;
        }
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
    public static async Task<Service?> GetAsync(string id, MasterDbContext masterDbContext)
    {
        return await masterDbContext.Services.AsNoTracking()
            .Include(x => x.Locations)
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    [Authorize]
    public static async Task<ResultDto<PageResultDto<Service>>> GetListAsync(int page, int pageSize,
        MasterDbContext masterDbContext)
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

        var result = await masterDbContext.Services
            .AsNoTracking()
            .Include(x => x.Locations)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var total = await masterDbContext.Services.CountAsync();

        return ResultDto<PageResultDto<Service>>.SuccessResult(new PageResultDto<Service>(result, total));
    }

    [Authorize]
    public static async Task<ResultDto<List<ServiceSelectDto>>> GetSelectListAsync(MasterDbContext masterDbContext)
    {
        var result = await masterDbContext.Services
            .AsNoTracking()
            .Select(x => new ServiceSelectDto()
            {
                Value = x.Id,
                Label = x.ServiceNames.First()
            })
            .ToListAsync();
        return ResultDto<List<ServiceSelectDto>>.SuccessResult(result);
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
    public static async Task StartServiceAsync(string id, MasterDbContext masterDbContext)
    {
        if (WebApplications.TryGetValue(id, out var app))
        {
            await app.StartAsync();
        }
        else
        {
            var service = await GetAsync(id, masterDbContext);

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
    public static async Task RestartServiceAsync(string id, MasterDbContext masterDbContext)
    {
        if (WebApplications.TryGetValue(id, out var app))
        {
            await app.StopAsync();

            var service = await GetAsync(id, masterDbContext);

            await Task.Factory.StartNew(BuilderService, service);

            await Task.Delay(500);
        }
    }

    private static async Task BuilderService(object state)
    {
        var service = (Service)state;

        var builder = WebApplication.CreateBuilder([]);

        FastContext.QpsService?.AddServiceQps(service.Id);

        IContentTypeProvider defaultContentTypeProvider = new DefaultContentTypeProvider();

        builder.WebHost.UseKestrel(options =>
        {
            options.ConfigureHttpsDefaults(adapterOptions =>
            {
                if (service.IsHttps)
                {
                    adapterOptions.ServerCertificateSelector = (context, name) =>
                        CertService.Certs.TryGetValue(name, out var cert)
                            ? new X509Certificate2(cert.File, cert.Password)
                            : new X509Certificate2(Path.Combine(AppContext.BaseDirectory, "gateway.pfx"), "010426");
                }
            });

            options.Listen(IPAddress.Parse("0.0.0.0"), service.Listen, listenOptions =>
            {
                if (!service.IsHttps) return;

                listenOptions.UseHttps();

                if (service.EnableHttp3)
                {
                    listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
                }
            });
        });

        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        // 绑定路由到service
        foreach (var location in service.Locations)
        {
            var clusterId = location.Id;
            var route = new RouteConfig()
            {
                RouteId = location.Id,
                ClusterId = clusterId,
                Match = new RouteMatch()
                {
                    Path = location.Path.TrimEnd('/') + "/{**catch-all}",
                    Hosts = service.ServiceNames
                }
            };
            routes.Add(route);
            var destinations = new Dictionary<string, DestinationConfig>();

            var cluster = new ClusterConfig()
            {
                Destinations = destinations,
                ClusterId = clusterId
            };


            if (location.Type == ApiServiceType.SingleService)
            {
                destinations.Add(location.ProxyPass, new DestinationConfig()
                {
                    Address = location.ProxyPass,
                    Host = new Uri(location.ProxyPass).Host,
                });
                clusters.Add(cluster);
                continue;
            }
            else if (location.Type == ApiServiceType.LoadBalance)
            {
                foreach (var upStream in location.UpStreams.Where(x => !string.IsNullOrEmpty(x.Server)))
                {
                    destinations.Add(upStream.Server!, new DestinationConfig()
                    {
                        Address = upStream.Server!,
                    });
                }

                clusters.Add(cluster);
            }
            else if (location.Type == ApiServiceType.StaticProxy)
            {
                routes.Remove(route);
            }
        }


        builder.Services.AddSingleton<ICurrentContext>(new CurrentContext()
        {
            ServiceId = service.Id,
        }).AddSingleton<StatisticsMiddleware>();

        builder.Services.AddReverseProxy()
            .LoadFromMemory(routes, clusters)
            // 删除所有代理的前缀
            .AddTransforms(context =>
            {
                var prefix = context.Route.Match.Path?.Replace("/{**catch-all}", "");
                if (!string.IsNullOrEmpty(prefix))
                {
                    context.AddPathRemovePrefix(prefix);
                }
            });

        var app = builder.Build();

        WebApplications.Add(service.Id, app);

        app.UseMiddleware<StatisticsMiddleware>();

        foreach (var location in service.Locations.Where(x => x.Type == ApiServiceType.StaticProxy))
        {
            app.Map(location.Path.TrimEnd('/'), app =>
            {
                app.Run((async context =>
                {
                    var path = Path.Combine(location.Root, context.Request.Path.Value[1..]);

                    if (File.Exists(path))
                    {
                        defaultContentTypeProvider.TryGetContentType(path, out var contentType);
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

                        defaultContentTypeProvider.TryGetContentType(tryPath, out var contentType);
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
}