using System.Text;
using Carrinho;
using Carrinho.DTO;
using Carrinho.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Npgsql.EntityFrameworkCore;
using Swashbuckle.AspNetCore;
using Swashbuckle.AspNetCore.Swagger;

using Microsoft.OpenApi.Models;

DotNetEnv.Env.Load();

var config =
    new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", true)
        .AddEnvironmentVariables()
        .Build();

var builder = WebApplication.CreateBuilder(args);
var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING") ??
                       builder.Configuration.GetConnectionString("DefaultConnection");
// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddDbContext<CartDb>(opt => opt.UseNpgsql(connectionString));
builder.Services.AddDbContext<UserDb>(opt => opt.UseNpgsql(connectionString));
builder.Services.AddAuthentication().AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        //ValidateLifetime = false,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
    };
});
builder.Services.AddHttpClient("OrderService", httpClient =>
{
    httpClient.BaseAddress = new Uri("http://localhost:5050/orders"); //might have to change this later

    httpClient.DefaultRequestHeaders.Add(
        HeaderNames.Accept, "application/json");
});
builder.Services.AddScoped<CartService>();

// ✅ Add CORS policy that allows everything (*)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .WithOrigins("http://localhost:5173", "http://localhost:3000")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("user_email", policy =>
        policy
            .RequireClaim("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"));
            //.RequireClaim("Email"));

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((doc, ctx, ct) =>
    {
        doc.Components ??= new Microsoft.OpenApi.Models.OpenApiComponents();

        doc.Components.SecuritySchemes["Bearer"] = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT Authorization header using the Bearer scheme."
        };

        doc.SecurityRequirements.Add(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });

        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseCors("AllowAll");
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    //app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
    });
}


var apiVersionOneMapping = app.MapGroup("/api/v1");
var cartMapping = apiVersionOneMapping.MapGroup("/cart");

cartMapping.MapGet("/", GetCart).RequireAuthorization("user_email");
cartMapping.MapPost("/", CreateCart).RequireAuthorization("user_email");
cartMapping.MapPatch("/cart-items", AddToCart).RequireAuthorization("user_email");
cartMapping.MapPatch("/cancel", CancelCart).RequireAuthorization("user_email");
cartMapping.MapPost("/checkout", CheckOutCart).RequireAuthorization("user_email");

static async Task<IResult> GetCart(HttpContext context, CartService cartService)
{
    var email = context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value;
    if (string.IsNullOrEmpty(email))
        return TypedResults.Unauthorized();
    try
    {
        var user = await cartService.getUser(email);
        var cart = await cartService.getCartIfExists(user);
        if (cart is null)
            return TypedResults.NotFound();
        return TypedResults.Ok(cart);
    }
    catch (Exception ex)
    {
        if (ex is KeyNotFoundException)
            return TypedResults.NotFound("User not found");
        return TypedResults.InternalServerError(ex.Message);
    }
}

//Two exceptions might happen inside which might be handled by a middleware
static async Task<IResult> CreateCart(HttpContext context, CartDto cartDto, CartService cartService)
{
    var email = context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value;
    if (string.IsNullOrEmpty(email))
        return TypedResults.Unauthorized();
    try
    {
        
        var newCart = await cartService.createCartIfNotExist(cartDto, email);
        return TypedResults.Ok(newCart);
    }
    catch (Exception ex)
    {
        if (ex is KeyNotFoundException)
            return TypedResults.NotFound("User not found");
        if (ex is InvalidOperationException)
            return TypedResults.BadRequest("User already has an valid cart");
        return TypedResults.InternalServerError(ex.Message);
    }
    return TypedResults.InternalServerError();
}
//TODO create exceptions for user not found or cart not found
static async Task<IResult> AddToCart(HttpContext context, CartItemDto[] cartItems, CartService cartService)
{
    var email = context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value;
    if (string.IsNullOrEmpty(email))
        return TypedResults.Unauthorized();
    try
    {
        var newCart = await cartService.addItemsToCart(cartItems, email);
        return TypedResults.Ok(newCart);
    }
    catch (Exception ex)
    {
        if (ex is KeyNotFoundException)
            return TypedResults.NotFound("Cart or user not found");
        return TypedResults.InternalServerError(ex.Message);
    }
    return TypedResults.InternalServerError();
}

static async Task<IResult> CancelCart(HttpContext context, CartService cartService)
{
    var email = context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value;
    if (string.IsNullOrEmpty(email))
        return TypedResults.Unauthorized();
    try
    {
        var oldCart = await cartService.cancelCartIfExist(email);
        return TypedResults.Ok(oldCart);
    }
    catch (Exception ex)
    {
        if (ex is KeyNotFoundException)
            return TypedResults.NotFound("Cart or user not found");
        return TypedResults.InternalServerError(ex.Message);
    }
    return TypedResults.InternalServerError();
}

static async Task<IResult> CheckOutCart(HttpContext context, CartService cartService)
{
    var email = context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name")?.Value;
    if (string.IsNullOrEmpty(email))
        return TypedResults.Unauthorized();
    try
    {
        var oldCart = await cartService.checkOutCart(email);
        return TypedResults.Ok(oldCart);
    }
    catch (Exception ex)
    {
        if (ex is KeyNotFoundException)
            return TypedResults.NotFound("Cart or user not found");
        if (ex is InvalidOperationException)
            return TypedResults.BadRequest("It was not possible to create an order from this cart.");
        return TypedResults.InternalServerError(ex.Message);
    }
    return TypedResults.InternalServerError();
}

app.Run();