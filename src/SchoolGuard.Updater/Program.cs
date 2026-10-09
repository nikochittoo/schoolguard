using SchoolGuard.Updater;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "SchoolGuardUpdater");
builder.Services.AddHostedService<UpdateWorker>();
builder.Build().Run();
