#region FreeSql类型转换

using Gateway.BackgroundServices;

Utils.TypeHandlers.TryAdd(typeof(Dictionary<string, string>), new StringJsonHandler<Dictionary<string, string>>());
Utils.TypeHandlers.TryAdd(typeof(RouteMatchEntity), new StringJsonHandler<RouteMatchEntity>());
Utils.TypeHandlers.TryAdd(typeof(List<DestinationsEntity>), new StringJsonHandler<List<DestinationsEntity>>());
Utils.TypeHandlers.TryAdd(typeof(string[]), new StringJsonHandler<string[]>());

#endregion

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.GetSection(nameof(JwtOptions))
    .Get<JwtOptions>();

builder.WebHost.UseKestrel(options =>
{
    // 配置多个域名证书
    options.ConfigureHttpsDefaults(adapterOptions =>
    {
        adapterOptions.ServerCertificateSelector = (_, name) =>
        {
            // 从Certificate服务中获取
            if (string.IsNullOrEmpty(name) ||
                !CertificateService.CertificateEntityDict.TryGetValue(name, out var certificate)) return new X509Certificate2();

            var path = Path.Combine("/data/", certificate.Path);

            if (File.Exists(path)) return new X509Certificate2(path, certificate.Password);

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"证书文件不存在：{path}");
            Console.ResetColor();
            throw new Exception($"证书文件不存在：{path}");
        };
    });
});

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = null;
    
    kestrel.ListenAnyIP(8081, portOptions =>
    {
        portOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
        portOptions.UseHttps();
    });

    kestrel.ListenAnyIP(8080, portOptions =>
    {
        portOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

#region Jwt

builder.Services
    .AddAuthorization()
    .AddJwtBearerAuthentication();

#endregion

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = int.MaxValue; 
});

builder.Services.Configure<FormOptions>(x =>
{
    x.ValueLengthLimit = int.MaxValue;
    x.MultipartBodyLengthLimit = int.MaxValue; // if don't set default value is: 128 MB
    x.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.Converters.Add(new JsonDateTimeConverter());
});

builder.Services.AddHostedService<GatewayBackgroundService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder => builder
            .SetIsOriginAllowed(_ => true)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials());
});

builder.Configuration.GetSection(nameof(RequestOptions)).Get<RequestOptions>();
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<RequestLogMiddleware>();
builder.Services.AddSingleton<StaticFileProxyMiddleware>();
builder.Services.AddSingleton<GatewayMiddleware>();

builder.Services.AddSingleton<RequestLogService>();
builder.Services.AddSingleton<GatewayService>();
builder.Services.AddSingleton<CertificateService>();
builder.Services.AddSingleton<FileStorageService>();
builder.Services.AddSingleton<StaticFileProxyService>();
builder.Services.AddSingleton<TestService>();
builder.Services.AddSingleton<SettingService>();
builder.Services.AddSingleton<AuthorityService>();
builder.Services.AddSingleton<NetWorkService>();

builder.Services.AddSingleton<IContentTypeProvider, FileExtensionContentTypeProvider>();

builder.Services.AddSingleton<IFreeSql>(_ =>
{
    var directory = new DirectoryInfo("/data");
    if (!directory.Exists)
    {
        directory.Create();
    }

    return new FreeSqlBuilder()
        .UseConnectionString(DataType.Sqlite, builder.Configuration.GetConnectionString("DefaultConnection"))
        .UseMonitorCommand(cmd => Console.WriteLine($"Sql：{cmd.CommandText}")) //监听SQL语句
        .UseAutoSyncStructure(true) //自动同步实体结构到数据库，FreeSql不会扫描程序集，只有CRUD时才会生成表。
        .Build();
});

// 使用内存加载配置 
builder.Services.AddReverseProxy()
    .LoadFromMemory(GatewayService.Routes, GatewayService.Clusters);

var app = builder.Build();

app.UseCors("AllowAll");

app.UseMiddleware<RequestLogMiddleware>();
app.UseMiddleware<GatewayMiddleware>();
app.UseMiddleware<StaticFileProxyMiddleware>();

// 配置MiniApis服务
app.MapRequestLog();
app.MapStaticFileProxy();
app.MapFileStorage();
app.MapGateway();
app.MapAuthority();
app.MapCertificate();
app.MapSetting();
app.MapNetWork();

app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();

await app.RunAsync();