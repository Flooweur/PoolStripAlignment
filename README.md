# Pool Test Strip Image Processor

A .NET 8 microservice that processes images of pool test strips by correcting their orientation and cropping them to show only the strip.

## Overview

This microservice provides a simple REST API that accepts photos of pool test strips and:

1. **Detects** the test strip in the image using contour analysis
2. **Corrects orientation** to make the strip vertical (with minimal rotation)
3. **Crops** the image to only include the test strip
4. **Returns** the processed image in PNG format

> **Note**: The algorithm makes the strip vertical but cannot determine which end is "up" without application-specific knowledge about the test pad color patterns. The strip will be oriented vertically with the smallest rotation angle applied.

## Features

- ✅ Automatic strip detection using edge detection and contour analysis
- ✅ Orientation correction using minAreaRect and rotation transformation
- ✅ Automatic cropping to strip area with configurable padding
- ✅ Support for multiple image formats (JPEG, PNG, GIF, WebP, BMP)
- ✅ Otsu's thresholding for robust detection under varying lighting
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

The image processing uses the following computer vision pipeline (OpenCV-based):

### 1. Preprocessing
- **Grayscale Conversion**: Using OpenCV's BGR2GRAY color conversion
- **Gaussian Blur**: 5x5 kernel to reduce noise while preserving edges
- **Otsu's Thresholding**: Automatically determines optimal threshold for binarization

### 2. Edge Detection & Contour Finding
- **Canny Edge Detection**: Dual threshold (50, 150) for robust edge detection
- **Morphological Dilation**: Closes gaps in contours (3x3 kernel, 2 iterations)
- **Contour Extraction**: External contours only (RetrievalModes.External)

### 3. Strip Identification
- **Contour Scoring**: Prefers elongated shapes with large area
- **MinAreaRect**: Gets the minimum area rotated bounding box for the strip
- Returns center, size (width, height), and rotation angle

### 4. Rotation Angle Calculation
The rotation angle is calculated to make the strip's long axis vertical:
```
1. Determine long axis direction from minAreaRect
2. Calculate angle from horizontal: θ_long = angle + 90° (if height > width)
3. Rotation needed = 90° - θ_long (normalized to [-90°, 90°])
```

### 5. Image Rotation
- **Affine Transformation**: Using OpenCV's GetRotationMatrix2D and WarpAffine
- **Canvas Expansion**: Output canvas is expanded to fit the rotated image
- **Border Handling**: White background fill for clean edges

### 6. Re-detection & Cropping
- Strip is re-detected in the rotated image for accurate bounds
- Crop rectangle is calculated with configurable padding (default: 10px)
- Final image is extracted and encoded as PNG

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
- **OpenCvSharp4**: .NET bindings for OpenCV (computer vision library)
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