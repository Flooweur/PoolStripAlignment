using Microsoft.AspNetCore.Mvc;
using PoolStripProcessor.Services;

// ============================================================================
// Pool Test Strip Image Processor - Main Entry Point
// ============================================================================
// This microservice accepts images of pool test strips and:
// 1. Corrects their orientation to be vertical and pointing upwards
// 2. Crops the image to only include the test strip
// 3. Returns the processed image
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Service Registration
// ---------------------------------------------------------------------------

// Register the image processing service
// Using Singleton because the service is stateless and thread-safe
builder.Services.AddSingleton<IStripImageProcessor, StripImageProcessor>();

// Add Swagger for API documentation (useful for development and testing)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Pool Strip Processor API",
        Version = "v1",
        Description = "A microservice that processes pool test strip images by correcting orientation and cropping to the strip area."
    });
});

// Configure request body size limit for image uploads (default is ~30MB)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50 MB max
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// Middleware Configuration
// ---------------------------------------------------------------------------

// Enable Swagger UI in all environments for easy testing
// In production, you might want to restrict this
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Pool Strip Processor API v1");
    c.RoutePrefix = string.Empty; // Serve Swagger UI at root
});

// ---------------------------------------------------------------------------
// API Endpoints
// ---------------------------------------------------------------------------

/// <summary>
/// Health check endpoint - useful for container orchestration (Docker, Kubernetes)
/// </summary>
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithTags("Health")
    .WithOpenApi(operation =>
    {
        operation.Summary = "Health check endpoint";
        operation.Description = "Returns the health status of the service. Use this for container health checks.";
        return operation;
    });

/// <summary>
/// Main endpoint: Process a pool test strip image
/// 
/// Accepts an image file upload, processes it to:
/// - Correct orientation (make vertical, pointing up)
/// - Crop to only the strip area
/// 
/// Returns the processed image as PNG
/// </summary>
app.MapPost("/process", async (
    IFormFile file,
    IStripImageProcessor processor,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    // Validate that a file was uploaded
    if (file == null || file.Length == 0)
    {
        logger.LogWarning("No file uploaded or file is empty");
        return Results.BadRequest(new { error = "No file uploaded or file is empty" });
    }

    // Validate file type (basic check based on content type)
    var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp" };
    if (!allowedTypes.Contains(file.ContentType?.ToLowerInvariant()))
    {
        logger.LogWarning("Invalid file type: {ContentType}", file.ContentType);
        return Results.BadRequest(new { 
            error = "Invalid file type. Allowed types: JPEG, PNG, GIF, WebP, BMP",
            receivedType = file.ContentType 
        });
    }

    logger.LogInformation("Processing image: {FileName}, Size: {Size} bytes, Type: {ContentType}",
        file.FileName, file.Length, file.ContentType);

    try
    {
        // Open the uploaded file stream
        using var inputStream = file.OpenReadStream();

        // Process the image
        var resultStream = await processor.ProcessStripImageAsync(inputStream, cancellationToken);

        logger.LogInformation("Successfully processed image: {FileName}", file.FileName);

        // Return the processed image as PNG
        return Results.File(
            resultStream,
            contentType: "image/png",
            fileDownloadName: $"processed_{Path.GetFileNameWithoutExtension(file.FileName)}.png"
        );
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error processing image: {FileName}", file.FileName);
        return Results.Problem(
            detail: "An error occurred while processing the image. Please ensure the image contains a visible test strip.",
            statusCode: 500,
            title: "Processing Error"
        );
    }
})
.WithName("ProcessStripImage")
.WithTags("Image Processing")
.DisableAntiforgery() // Required for file uploads in minimal APIs
.Accepts<IFormFile>("multipart/form-data")
.Produces(200, contentType: "image/png")
.Produces<ProblemDetails>(400)
.Produces<ProblemDetails>(500)
.WithOpenApi(operation =>
{
    operation.Summary = "Process a pool test strip image";
    operation.Description = @"
Upload an image containing a pool test strip. The service will:

1. **Detect** the test strip in the image
2. **Correct orientation** to make the strip vertical and pointing upwards
3. **Crop** the image to only include the test strip

### Supported Image Formats
- JPEG/JPG
- PNG
- GIF
- WebP
- BMP

### Input Requirements
- The test strip should be visible and roughly vertical in the image
- Good lighting and contrast recommended for best results

### Output
- Returns the processed image in PNG format
";
    return operation;
});

// ---------------------------------------------------------------------------
// Run the Application
// ---------------------------------------------------------------------------

app.Run();