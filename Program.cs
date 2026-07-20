using zip02.Services.Events;
using zip02.Services.Notifications.InMemory;
using zip02.Services.Payments.InMemory;
using zip02.Services.Refunds.Infrastructure;
using zip02.Services.Ticketing.InMemory;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
builder.Services.AddSingleton<ITicketStore, InMemoryTicketStore>();
builder.Services.AddSingleton<IPaymentStore, InMemoryPaymentStore>();
builder.Services.AddSingleton<INotificationStore, InMemoryNotificationStore>();
builder.Services.AddSingleton<IRefundProcessor, InMemoryRefundProcessor>();
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
