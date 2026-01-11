namespace PoolStripProcessor.Services;

/// <summary>
/// Interface for the pool test strip image processing service.
/// Defines the contract for processing uploaded strip images.
/// </summary>
public interface IStripImageProcessor
{
    /// <summary>
    /// Processes a pool test strip image by:
    /// 1. Detecting the strip in the image
    /// 2. Correcting its orientation to be vertical and pointing upwards
    /// 3. Cropping the image to only include the strip
    /// </summary>
    /// <param name="imageStream">The input stream containing the image data</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>A stream containing the processed image in PNG format</returns>
    Task<Stream> ProcessStripImageAsync(Stream imageStream, CancellationToken cancellationToken = default);
}