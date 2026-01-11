using OpenCvSharp;

namespace PoolStripProcessor.Services;

/// <summary>
/// Implementation of the pool test strip image processor using OpenCV.
/// 
/// ALGORITHM OVERVIEW (Industry-Standard Computer Vision Pipeline):
/// 1. Load image and convert to grayscale
/// 2. Apply Gaussian blur to reduce noise
/// 3. Use Canny edge detection to find edges
/// 4. Find contours in the edge image
/// 5. Identify the largest contour (the strip)
/// 6. Use minAreaRect to get the minimum area rotated bounding box
/// 7. Rotate the image to make the strip vertical
/// 8. Crop to the bounding box
/// </summary>
public class StripImageProcessor : IStripImageProcessor
{
    // Padding to add around the cropped strip (in pixels)
    private const int CropPadding = 5;

    // Canny edge detection thresholds
    private const double CannyThreshold1 = 50;
    private const double CannyThreshold2 = 150;

    // Gaussian blur kernel size (must be odd)
    private const int BlurKernelSize = 5;

    private readonly ILogger<StripImageProcessor> _logger;

    public StripImageProcessor(ILogger<StripImageProcessor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Stream> ProcessStripImageAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting strip image processing with OpenCV");

        // Read stream into byte array for OpenCV
        using var memoryStream = new MemoryStream();
        await imageStream.CopyToAsync(memoryStream, cancellationToken);
        var imageBytes = memoryStream.ToArray();

        // Load image with OpenCV
        using var originalImage = Cv2.ImDecode(imageBytes, ImreadModes.Color);
        if (originalImage.Empty())
        {
            throw new InvalidOperationException("Failed to decode image");
        }

        _logger.LogDebug("Loaded image: {Width}x{Height}", originalImage.Width, originalImage.Height);

        // Step 1: Detect the strip and get its rotated bounding box
        var stripRect = DetectStripRect(originalImage);
        _logger.LogDebug("Detected strip - Center: ({CenterX}, {CenterY}), Size: {Width}x{Height}, Angle: {Angle}°",
            stripRect.Center.X, stripRect.Center.Y, stripRect.Size.Width, stripRect.Size.Height, stripRect.Angle);

        // Step 2: Calculate the rotation angle to make the strip vertical
        double rotationAngle = CalculateRotationAngle(stripRect);
        _logger.LogDebug("Rotation angle to make vertical: {Angle}°", rotationAngle);

        // Step 3: Rotate the image
        using var rotatedImage = RotateImage(originalImage, rotationAngle);

        // Step 4: Calculate the crop rectangle in the rotated image
        var cropRect = CalculateCropRectangle(stripRect, rotationAngle, originalImage.Size(), rotatedImage.Size());
        _logger.LogDebug("Crop rectangle: X={X}, Y={Y}, W={Width}, H={Height}",
            cropRect.X, cropRect.Y, cropRect.Width, cropRect.Height);

        // Step 5: Crop the image
        using var croppedImage = CropImage(rotatedImage, cropRect);

        // Step 6: Encode and return as PNG
        Cv2.ImEncode(".png", croppedImage, out var outputBytes);
        var outputStream = new MemoryStream(outputBytes);

        _logger.LogInformation("Strip image processing completed successfully. Output: {Width}x{Height}",
            croppedImage.Width, croppedImage.Height);

        return outputStream;
    }

    /// <summary>
    /// Detects the pool strip using edge detection and contour analysis.
    /// Returns the minimum area rotated rectangle that bounds the strip.
    /// </summary>
    private RotatedRect DetectStripRect(Mat image)
    {
        // Convert to grayscale
        using var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

        // Apply Gaussian blur to reduce noise
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(BlurKernelSize, BlurKernelSize), 0);

        // Apply Canny edge detection
        using var edges = new Mat();
        Cv2.Canny(blurred, edges, CannyThreshold1, CannyThreshold2);

        // Optional: Dilate edges to close gaps
        using var dilated = new Mat();
        var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        Cv2.Dilate(edges, dilated, kernel, iterations: 2);

        // Find contours
        Cv2.FindContours(dilated, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        if (contours.Length == 0)
        {
            _logger.LogWarning("No contours found, returning full image bounds");
            return new RotatedRect(
                new Point2f(image.Width / 2f, image.Height / 2f),
                new Size2f(image.Width, image.Height),
                0);
        }

        // Find the largest contour by area (this should be the strip)
        var largestContour = contours
            .OrderByDescending(c => Cv2.ContourArea(c))
            .First();

        double area = Cv2.ContourArea(largestContour);
        _logger.LogDebug("Largest contour area: {Area} pixels", area);

        // Get the minimum area rotated rectangle that bounds this contour
        var minRect = Cv2.MinAreaRect(largestContour);

        return minRect;
    }

    /// <summary>
    /// Calculates the angle needed to rotate the strip to be vertical.
    /// OpenCV's minAreaRect returns angles in the range [-90, 0).
    /// </summary>
    private double CalculateRotationAngle(RotatedRect rect)
    {
        double angle = rect.Angle;
        float width = rect.Size.Width;
        float height = rect.Size.Height;

        // minAreaRect returns the angle of the rectangle's width edge relative to horizontal
        // We need to adjust based on which dimension is longer (the strip's length)
        
        if (width < height)
        {
            // Height is the long axis (strip is more vertical than horizontal)
            // Angle is already relative to vertical, just need minor adjustment
            // OpenCV angle: -90 means vertical, 0 means horizontal
            angle = angle + 90;
        }
        else
        {
            // Width is the long axis (strip is more horizontal)
            // Need to rotate by angle to make it vertical
            // No adjustment needed, angle directly gives us the rotation
        }

        // Normalize to [-45, 45] range for minimal rotation
        while (angle > 45) angle -= 90;
        while (angle < -45) angle += 90;

        return angle;
    }

    /// <summary>
    /// Rotates an image by the specified angle around its center.
    /// The output image is expanded to fit the rotated content.
    /// </summary>
    private Mat RotateImage(Mat image, double angle)
    {
        var center = new Point2f(image.Width / 2f, image.Height / 2f);
        
        // Get the rotation matrix
        using var rotationMatrix = Cv2.GetRotationMatrix2D(center, -angle, 1.0);
        
        // Calculate new image bounds after rotation
        double cos = Math.Abs(rotationMatrix.At<double>(0, 0));
        double sin = Math.Abs(rotationMatrix.At<double>(0, 1));
        int newWidth = (int)(image.Width * cos + image.Height * sin);
        int newHeight = (int)(image.Width * sin + image.Height * cos);
        
        // Adjust the rotation matrix to account for the new center
        rotationMatrix.At<double>(0, 2) += (newWidth - image.Width) / 2.0;
        rotationMatrix.At<double>(1, 2) += (newHeight - image.Height) / 2.0;

        // Apply the rotation
        var rotated = new Mat();
        Cv2.WarpAffine(image, rotated, rotationMatrix, new Size(newWidth, newHeight),
            InterpolationFlags.Linear, BorderTypes.Constant, Scalar.Black);

        return rotated;
    }

    /// <summary>
    /// Calculates the crop rectangle in the rotated image space.
    /// Transforms the original bounding box corners through the rotation.
    /// </summary>
    private Rect CalculateCropRectangle(RotatedRect originalRect, double rotationAngle,
        Size originalImageSize, Size rotatedImageSize)
    {
        // Get the 4 corners of the rotated rectangle
        var corners = originalRect.Points();

        // Transform corners through the rotation
        double angleRad = -rotationAngle * Math.PI / 180.0;
        double cos = Math.Cos(angleRad);
        double sin = Math.Sin(angleRad);

        var origCenter = new Point2f(originalImageSize.Width / 2f, originalImageSize.Height / 2f);
        var newCenter = new Point2f(rotatedImageSize.Width / 2f, rotatedImageSize.Height / 2f);

        var transformedCorners = new List<Point2f>();
        foreach (var corner in corners)
        {
            // Translate to origin
            float dx = corner.X - origCenter.X;
            float dy = corner.Y - origCenter.Y;

            // Rotate
            float rx = (float)(dx * cos - dy * sin);
            float ry = (float)(dx * sin + dy * cos);

            // Translate to new center
            transformedCorners.Add(new Point2f(rx + newCenter.X, ry + newCenter.Y));
        }

        // Calculate axis-aligned bounding box of transformed corners
        float minX = transformedCorners.Min(p => p.X);
        float maxX = transformedCorners.Max(p => p.X);
        float minY = transformedCorners.Min(p => p.Y);
        float maxY = transformedCorners.Max(p => p.Y);

        // Apply padding and clamp to image bounds
        int x = Math.Max(0, (int)Math.Floor(minX) - CropPadding);
        int y = Math.Max(0, (int)Math.Floor(minY) - CropPadding);
        int right = Math.Min(rotatedImageSize.Width, (int)Math.Ceiling(maxX) + CropPadding);
        int bottom = Math.Min(rotatedImageSize.Height, (int)Math.Ceiling(maxY) + CropPadding);

        return new Rect(x, y, right - x, bottom - y);
    }

    /// <summary>
    /// Crops the image to the specified rectangle.
    /// </summary>
    private Mat CropImage(Mat image, Rect cropRect)
    {
        // Ensure crop rect is within image bounds
        cropRect = cropRect.Intersect(new Rect(0, 0, image.Width, image.Height));

        if (cropRect.Width <= 0 || cropRect.Height <= 0)
        {
            _logger.LogWarning("Invalid crop rectangle, returning original image");
            return image.Clone();
        }

        return new Mat(image, cropRect);
    }
}
