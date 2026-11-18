using System.Text;
using System.Text.Json.Serialization;
using Ecommerce.Api.Contracts;
using Ecommerce.Api.Security;
using Ecommerce.Application;
using Ecommerce.Shared.Results;
using Ecommerce.Infrastructure;
using Ecommerce.Infrastructure.Options;
using Ecommerce.Shared.Cqrs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddCqrs(AssemblyReference.Assembly);
builder.Services.AddInfrastructure(builder.Configuration);

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            // The subject must be a UUID (handoff section 2); anything else is an invalid token.
            OnTokenValidated = context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out _))
                {
                    context.Fail("sub is not a UUID.");
                }

                return Task.CompletedTask;
            },
            // Every authentication failure answers the spec envelope: 401 E10002.
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var error = Error.Unauthorized("Token.Invalid", "Invalid or expired access token.");
                var requestId = context.Request.Headers.TryGetValue("requestId", out var id) && !string.IsNullOrWhiteSpace(id)
                    ? id.ToString()
                    : $"req-{Guid.NewGuid()}";
                var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(ApiEnvelope.Failure(error, requestId, now));
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddPublicRateLimits(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
