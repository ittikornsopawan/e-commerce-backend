using System.Text;
using Ecommerce.Api.Contracts;
using Ecommerce.Api.Extensions;
using Ecommerce.Api.Security;
using Ecommerce.Application;
using Ecommerce.Shared.Results;
using Ecommerce.Infrastructure;
using Ecommerce.Infrastructure.Options;
using Ecommerce.Shared.Cqrs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(o => o.Filters.Add<Ecommerce.Api.Filters.RequiredHeadersFilter>())
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = Ecommerce.Api.Filters.InvalidBody.Response);
builder.Services.AddExceptionHandler<Ecommerce.Api.Filters.EnvelopeExceptionHandler>();
builder.Services.AddProblemDetails(); // required by UseExceptionHandler; EnvelopeExceptionHandler answers first
builder.Services.AddOpenApi();

builder.Services.AddCqrs(AssemblyReference.Assembly);
builder.Services.AddScoped<Ecommerce.Application.Features.Authen.Common.CallerCredential>();
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
                await HttpEnvelope.WriteFailureAsync(context.HttpContext, Error.Unauthorized("Token.Invalid", "Invalid or expired access token."));
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
