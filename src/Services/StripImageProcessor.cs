using OpenCvSharp;

namespace PoolStripProcessor.Services;

/// <summary>
/// Implementation of the pool test strip image processor using OpenCV.
/// 
/// ALGORITHM OVERVIEW (Industry-Standard Computer Vision Pipeline):
/// 1. Load image and convert to grayscale
/// 2. Apply Gaussian blur to reduce noise
/// 3. Use adaptive thresholding or Otsu's method for robust binarization
/// 4. Use Canny edge detection to find edges
/// 5. Find contours in the edge image
/// 6. Identify the largest elongated contour (the strip)
/// 7. Use minAreaRect to get the minimum area rotated bounding box
/// 8. Calculate rotation angle to make the strip vertical
/// 9. Rotate the image to make the strip vertical
/// 10. Crop to the bounding box
/// 
/// NOTE ON "POINTING UPWARDS":
/// True orientation detection (determining which end is "up") would require
/// analyzing the color pattern of the test pads, which is application-specific.
/// This implementation makes the strip vertical with minimal rotation.
/// </summary>
public class StripImageProcessor : IStripImageProcessor
{
    // Padding to add around the cropped strip (in pixels)
    private const int CropPadding = 10;

    // Canny edge detection thresholds
    private const double CannyThreshold1 = 50;
    private const double CannyThreshold2 = 150;

    // Gaussian blur kernel size (must be odd)
    private const int BlurKernelSize = 5;

    // Minimum aspect ratio to consider a contour as a strip (length/width)
    private const double MinStripAspectRatio = 2.0;

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
        double rotationAngle = CalculateRotationAngleForVertical(stripRect);
        _logger.LogDebug("Rotation angle to make vertical: {Angle}°", rotationAngle);

        // Step 3: Rotate the image
        using var rotatedImage = RotateImage(originalImage, rotationAngle);

        // Step 4: Re-detect the strip in the rotated image to get accurate crop bounds
        var rotatedStripRect = DetectStripRect(rotatedImage);
        
        // Step 5: Calculate the crop rectangle from the now-vertical strip
        var cropRect = CalculateCropRectangleFromVerticalStrip(rotatedStripRect, rotatedImage.Size());
        _logger.LogDebug("Crop rectangle: X={X}, Y={Y}, W={Width}, H={Height}",
            cropRect.X, cropRect.Y, cropRect.Width, cropRect.Height);

        // Step 6: Crop the image
        using var croppedImage = CropImage(rotatedImage, cropRect);

        // Step 7: Encode and return as PNG
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

        // Apply Otsu's thresholding for better binarization
        using var binary = new Mat();
        Cv2.Threshold(blurred, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        // Apply Canny edge detection on the binary image
        using var edges = new Mat();
        Cv2.Canny(binary, edges, CannyThreshold1, CannyThreshold2);

        // Dilate edges to close gaps
        using var dilated = new Mat();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
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

        // Find the best contour - prefer elongated shapes (strips)
        RotatedRect? bestRect = null;
        double bestScore = 0;

        foreach (var contour in contours)
        {
            double area = Cv2.ContourArea(contour);
            if (area < 100) continue; // Skip tiny contours

            var rect = Cv2.MinAreaRect(contour);
            
            // Calculate aspect ratio (always > 1)
            float longSide = Math.Max(rect.Size.Width, rect.Size.Height);
            float shortSide = Math.Min(rect.Size.Width, rect.Size.Height);
            double aspectRatio = shortSide > 0 ? longSide / shortSide : 1;

            // Score: prefer large area and elongated shape
            double score = area * Math.Min(aspectRatio, 10); // Cap aspect ratio contribution

            if (score > bestScore)
            {
                bestScore = score;
                bestRect = rect;
            }
        }

        if (bestRect == null)
        {
            _logger.LogWarning("No suitable contour found, using largest by area");
            var largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
            bestRect = Cv2.MinAreaRect(largestContour);
        }

        double finalArea = bestRect.Value.Size.Width * bestRect.Value.Size.Height;
        _logger.LogDebug("Selected contour - Area: {Area} pixels, AspectRatio: {AR:F2}", 
            finalArea,
            Math.Max(bestRect.Value.Size.Width, bestRect.Value.Size.Height) / 
            Math.Max(1, Math.Min(bestRect.Value.Size.Width, bestRect.Value.Size.Height)));

        return bestRect.Value;
    }

    /// <summary>
    /// Calculates the angle needed to rotate the strip to be vertical.
    /// 
    /// OpenCV minAreaRect angle convention (OpenCV 4.x):
    /// - Returns a RotatedRect with center, size (width, height), and angle
    /// - The angle is the rotation angle of the rectangle in degrees
    /// - Angle is measured counter-clockwise from the horizontal axis to the first side (width)
    /// - Angle range is typically [-90, 0) but can vary
    /// 
    /// To make the long axis vertical:
    /// 1. Determine which dimension (width or height) is the long axis
    /// 2. Calculate the current angle of the long axis from vertical
    /// 3. Return the rotation needed to make it vertical
    /// </summary>
    private double CalculateRotationAngleForVertical(RotatedRect rect)
    {
        float width = rect.Size.Width;
        float height = rect.Size.Height;
        double angle = rect.Angle;

        _logger.LogDebug("MinAreaRect - Width: {W}, Height: {H}, Angle: {A}°", width, height, angle);

        // Determine the angle of the LONG axis from horizontal
        // In OpenCV's minAreaRect:
        // - angle is the rotation of the width-edge from horizontal
        // - If width > height: the long axis is at `angle` degrees from horizontal
        // - If height > width: the long axis is at `angle + 90` degrees from horizontal
        
        double longAxisAngleFromHorizontal;
        
        if (width >= height)
        {
            // Width is the long axis
            // The width edge is at `angle` degrees from horizontal
            longAxisAngleFromHorizontal = angle;
        }
        else
        {
            // Height is the long axis
            // The height edge is perpendicular to width, so at `angle + 90` from horizontal
            longAxisAngleFromHorizontal = angle + 90;
        }

        // Normalize to [-180, 180]
        while (longAxisAngleFromHorizontal > 180) longAxisAngleFromHorizontal -= 360;
        while (longAxisAngleFromHorizontal <= -180) longAxisAngleFromHorizontal += 360;

        _logger.LogDebug("Long axis angle from horizontal: {Angle}°", longAxisAngleFromHorizontal);

        // To make the long axis vertical (pointing up or down), we need it at 90° or -90° from horizontal
        // Calculate rotation needed to reach 90° (vertical)
        double rotationNeeded = 90 - longAxisAngleFromHorizontal;

        // Normalize to [-90, 90] for minimal rotation
        // We can rotate by adding/subtracting 180° to flip, but we want minimal rotation
        while (rotationNeeded > 90) rotationNeeded -= 180;
        while (rotationNeeded < -90) rotationNeeded += 180;

        _logger.LogDebug("Rotation needed for vertical: {Angle}°", rotationNeeded);

        return rotationNeeded;
    }

    /// <summary>
    /// Rotates an image by the specified angle around its center.
    /// The output image is expanded to fit the rotated content.
    /// </summary>
    /// <param name="image">Source image</param>
    /// <param name="angle">Rotation angle in degrees (positive = counter-clockwise)</param>
    private Mat RotateImage(Mat image, double angle)
    {
        var center = new Point2f(image.Width / 2f, image.Height / 2f);
        
        // Get the rotation matrix
        // Note: OpenCV's getRotationMatrix2D uses positive angles for counter-clockwise rotation
        using var rotationMatrix = Cv2.GetRotationMatrix2D(center, angle, 1.0);
        
        // Calculate new image bounds after rotation
        double angleRad = Math.Abs(angle) * Math.PI / 180.0;
        double cos = Math.Cos(angleRad);
        double sin = Math.Sin(angleRad);
        int newWidth = (int)Math.Ceiling(image.Width * cos + image.Height * sin);
        int newHeight = (int)Math.Ceiling(image.Width * sin + image.Height * cos);
        
        // Adjust the rotation matrix to account for the translation to the new center
        double tx = (newWidth - image.Width) / 2.0;
        double ty = (newHeight - image.Height) / 2.0;
        rotationMatrix.At<double>(0, 2) += tx;
        rotationMatrix.At<double>(1, 2) += ty;

        // Apply the rotation with white background (better for strips on light backgrounds)
        var rotated = new Mat();
        Cv2.WarpAffine(image, rotated, rotationMatrix, new Size(newWidth, newHeight),
            InterpolationFlags.Linear, BorderTypes.Constant, Scalar.White);

        return rotated;
    }

    /// <summary>
    /// Calculates the crop rectangle for a now-vertical strip.
    /// Uses the axis-aligned bounding box of the rotated rectangle.
    /// </summary>
    private Rect CalculateCropRectangleFromVerticalStrip(RotatedRect stripRect, Size imageSize)
    {
        // Get the 4 corners of the rotated rectangle
        var corners = stripRect.Points();

        // Calculate axis-aligned bounding box
        float minX = corners.Min(p => p.X);
        float maxX = corners.Max(p => p.X);
        float minY = corners.Min(p => p.Y);
        float maxY = corners.Max(p => p.Y);

        // Apply padding and clamp to image bounds
        int x = Math.Max(0, (int)Math.Floor(minX) - CropPadding);
        int y = Math.Max(0, (int)Math.Floor(minY) - CropPadding);
        int right = Math.Min(imageSize.Width, (int)Math.Ceiling(maxX) + CropPadding);
        int bottom = Math.Min(imageSize.Height, (int)Math.Ceiling(maxY) + CropPadding);

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
