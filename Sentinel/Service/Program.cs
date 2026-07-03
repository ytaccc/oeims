using Microsoft.AspNetCore.Builder;
using OEIMS.Sentinel.Service;

var builder = WebApplication.CreateBuilder(args);

builder.AddExamMonitoringService();

var app = builder.Build();

app.UseExamMonitoringService();

app.Run();