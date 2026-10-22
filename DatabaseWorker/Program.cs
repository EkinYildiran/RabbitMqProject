using DatabaseWorker;
using DatabaseWorker.Data;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection") // baðlantý bilgisini  appsettings.json dan okur
    )
);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
