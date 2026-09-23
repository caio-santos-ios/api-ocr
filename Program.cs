using api_ocr.src.Services;
using DotNetEnv;

var builder = WebApplication.CreateBuilder(args);

Env.Load();

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddSingleton<OcrService>();
builder.Services.AddSingleton<ImplanteReader>();

var allowedOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "http://localhost:3000").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
Console.WriteLine(
    $"ALLOWED_ORIGINS: {Environment.GetEnvironmentVariable("ALLOWED_ORIGINS")}"
);
builder.Services.AddCors(options =>
{
    options.AddPolicy("AppPolicy", policy =>
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseHttpsRedirection();

app.UseCors("AppPolicy");

app.MapControllers();
app.Run();