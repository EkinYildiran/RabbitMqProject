using SyntheticDataWorker;
using SyntheticDataWorker.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
