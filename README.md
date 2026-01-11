# Pool Test Strip Image Processor

A .NET 8 microservice that processes images of pool test strips by correcting their orientation and cropping them to show only the strip.

## Overview

This microservice provides a simple REST API that accepts photos of pool test strips and:

1. **Detects** the test strip in the image
2. **Corrects orientation** to make the strip vertical and pointing upwards
3. **Crops** the image to only include the test strip
4. **Returns** the processed image in PNG format

## Features

- ✅ Automatic strip detection using image analysis
- ✅ Orientation correction using image moments algorithm
- ✅ Automatic cropping to strip area
- ✅ Support for multiple image formats (JPEG, PNG, GIF, WebP, BMP)
- ✅ Docker-ready with multi-stage build
- ✅ Swagger/OpenAPI documentation
- ✅ Health check endpoint for container orchestration
- ✅ Comprehensive logging

## Quick Start

### Using Docker (Recommended)

```bash
# Build and run with Docker Compose
docker-compose up --build

# The API will be available at http://localhost:5000
# Swagger UI at http://localhost:5000/
```

### Without Docker

Prerequisites:
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
# Navigate to source directory
cd src

# Restore packages
dotnet restore

# Run the application
dotnet run

# The API will be available at http://localhost:5000 (or the port shown in console)
```

## API Reference

### Health Check

```http
GET /health
```

Returns the health status of the service.

**Response:**
```json
{
  "status": "healthy",
  "timestamp": "2024-01-11T12:00:00Z"
}
```

### Process Strip Image

```http
POST /process
Content-Type: multipart/form-data
```

Upload an image containing a pool test strip. The service will process it and return the corrected, cropped image.

**Request:**
- `file`: The image file (multipart/form-data)

**Response:**
- Content-Type: `image/png`
- The processed image as a PNG file

**Example using cURL:**

```bash
curl -X POST http://localhost:5000/process \
  -F "file=@/path/to/your/strip-image.jpg" \
  --output processed-strip.png
```

**Example using PowerShell:**

```powershell
Invoke-RestMethod -Uri "http://localhost:5000/process" `
  -Method Post `
  -InFile "C:\path\to\your\strip-image.jpg" `
  -ContentType "multipart/form-data" `
  -OutFile "processed-strip.png"
```

## Algorithm Details

The image processing uses several computer vision techniques:

### 1. Grayscale Conversion
The image is converted to grayscale using the luminance formula:
```
Y = 0.299×R + 0.587×G + 0.114×B
```

### 2. Otsu's Thresholding
Automatically determines the optimal threshold to separate the strip from the background by minimizing intra-class variance. This adapts to different lighting conditions.

### 3. Image Moments
Calculates statistical moments of the binary image:
- **M00**: Total area (pixel count)
- **M10, M01**: First moments (for centroid calculation)
- **M20, M02, M11**: Second moments (for orientation calculation)

### 4. Orientation Angle
The principal axis angle is calculated from central moments:
```
θ = 0.5 × atan2(2×μ11, μ20 - μ02)
```

### 5. Rotation & Cropping
The image is rotated to make the strip vertical, then cropped to the bounding box of the strip area with a small padding.

## Project Structure

```
/
├── src/
│   ├── Program.cs              # Application entry point & API configuration
│   ├── Services/
│   │   ├── IStripImageProcessor.cs  # Service interface
│   │   └── StripImageProcessor.cs   # Image processing implementation
│   ├── PoolStripProcessor.csproj    # Project file
│   ├── appsettings.json             # Production settings
│   └── appsettings.Development.json # Development settings
├── Dockerfile              # Multi-stage Docker build
├── docker-compose.yml      # Docker Compose configuration
├── .dockerignore           # Docker build exclusions
└── README.md               # This file
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Runtime environment (Development/Production) | Production |
| `ASPNETCORE_URLS` | URL bindings | http://+:8080 (in container) |
| `Logging__LogLevel__Default` | Default log level | Information |

### Docker Compose Customization

Edit `docker-compose.yml` to customize:
- Port mapping (default: 5000:8080)
- Memory limits (default: 512MB limit, 128MB reserved)
- Log rotation settings

## Development

### Building

```bash
cd src
dotnet build
```

### Running Tests

```bash
# If you add tests later
dotnet test
```

### Debugging

Set `ASPNETCORE_ENVIRONMENT=Development` for:
- More verbose logging
- Detailed error messages
- Swagger UI at root path

## Docker Commands

```bash
# Build the image
docker build -t pool-strip-processor .

# Run directly
docker run -p 5000:8080 pool-strip-processor

# Build and run with compose
docker-compose up --build

# Run in background
docker-compose up -d

# View logs
docker-compose logs -f

# Stop
docker-compose down

# Rebuild without cache
docker-compose build --no-cache
```

## Troubleshooting

### Image not detected properly

- Ensure the test strip has good contrast against the background
- The strip should be the dominant elongated object in the image
- Avoid reflections or shadows on the strip

### High memory usage

- Large images (>20MP) may require more memory
- Adjust Docker memory limits in `docker-compose.yml`

### Slow processing

- Processing time depends on image size
- Consider resizing very large images before uploading

## Technology Stack

- **.NET 8**: Runtime and framework
- **ASP.NET Core Minimal APIs**: Web framework
- **SixLabors.ImageSharp**: Cross-platform image processing
- **Docker**: Containerization
- **Swagger/OpenAPI**: API documentation

## License

This project is provided as-is for educational and practical use.

## Contributing

Contributions are welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request