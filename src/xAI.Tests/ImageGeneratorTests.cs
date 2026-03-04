using Grpc.Core;
using Microsoft.Extensions.AI;
using Moq;
using Tests.Client.Helpers;
using xAI;
using xAI.Protocol;
using static ConfigurationExtensions;

namespace xAI.Tests;

public class ImageGeneratorTests(ITestOutputHelper output)
{
    [SecretsFact("XAI_API_KEY")]
    public async Task GenerateImage_WithPrompt_ReturnsImageContent()
    {
        var imageGenerator = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIImageGenerator("grok-imagine-image");

        var request = new ImageGenerationRequest("A cat sitting on a tree branch");
        var options = new ImageGenerationOptions
        {
            ResponseFormat = ImageGenerationResponseFormat.Uri,
        };

        var response = await imageGenerator.GenerateAsync(request, options);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);
        Assert.Single(response.Contents);

        var image = response.Contents.First();
        Assert.True(image is UriContent);

        var uriContent = (UriContent)image;
        Assert.NotNull(uriContent.Uri);

        output.WriteLine($"Generated image URL: {uriContent.Uri}");
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GenerateImage_WithEditsToPreviousImage()
    {
        var imageGenerator = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIImageGenerator("grok-imagine-image");

        var request = new ImageGenerationRequest("A cat sitting on a tree branch");
        var options = new ImageGenerationOptions
        {
            MediaType = "image/png",
            ResponseFormat = ImageGenerationResponseFormat.Uri,
            Count = 1
        };

        var response = await imageGenerator.GenerateAsync(request, options);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);
        Assert.Single(response.Contents);
        var image = Assert.IsType<UriContent>(response.Contents.First());
        // media type in options is ignored and you always get the same jpg
        Assert.Equal("image/jpeg", image.MediaType);
        output.WriteLine($"Generated image URL: {image.Uri}");

        var edit = await imageGenerator.GenerateAsync(new ImageGenerationRequest("Edit provided image by adding a batman mask", [image]), options);

        Assert.NotNull(edit);
        Assert.NotEmpty(edit.Contents);
        Assert.Single(edit.Contents);
        image = Assert.IsType<UriContent>(edit.Contents.First());
        // media type in options is ignored and you always get the same jpg
        Assert.Equal("image/jpeg", image.MediaType);

        output.WriteLine($"Edited image URL: {image.Uri}");
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GenerateImage_WithBase64Response_ReturnsDataContent()
    {
        var imageGenerator = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIImageGenerator("grok-imagine-image");

        var request = new ImageGenerationRequest("A sunset over mountains");
        var options = new ImageGenerationOptions
        {
            ResponseFormat = ImageGenerationResponseFormat.Data,
            Count = 1
        };

        var response = await imageGenerator.GenerateAsync(request, options);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);
        Assert.Single(response.Contents);

        var image = response.Contents.First();
        Assert.True(image is DataContent);

        var dataContent = (DataContent)image;
        Assert.True(dataContent.Data.Length > 0);
        Assert.Equal("image/jpeg", dataContent.MediaType);

        output.WriteLine($"Generated image size: {dataContent.Data.Length} bytes");
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GenerateImage_DefaultsToUriContent()
    {
        var imageGenerator = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIImageGenerator("grok-imagine-image");

        var request = new ImageGenerationRequest("A sunset over mountains");
        var response = await imageGenerator.GenerateAsync(request);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);
        Assert.Single(response.Contents);

        Assert.IsType<UriContent>(response.Contents.First());
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GenerateMultipleImages_ReturnsCorrectCount()
    {
        var imageGenerator = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIImageGenerator("grok-imagine-image");

        var request = new ImageGenerationRequest("A robot reading a book");
        var options = new ImageGenerationOptions
        {
            ResponseFormat = ImageGenerationResponseFormat.Uri,
            Count = 3
        };

        var response = await imageGenerator.GenerateAsync(request, options);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);
        Assert.Equal(3, response.Contents.Count);

        foreach (var image in response.Contents)
        {
            Assert.True(image is UriContent);
            output.WriteLine($"Image URL: {((UriContent)image).Uri}");
        }
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GenerateImage_ResponseContainsRawRepresentation()
    {
        var imageGenerator = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIImageGenerator("grok-imagine-image");

        var request = new ImageGenerationRequest("A futuristic cityscape");
        var options = new ImageGenerationOptions
        {
            ResponseFormat = ImageGenerationResponseFormat.Uri
        };

        var response = await imageGenerator.GenerateAsync(request, options);

        Assert.NotNull(response);
        Assert.NotNull(response.RawRepresentation);

        // The raw representation should be an ImageResponse from the protocol
        var rawResponse = Assert.IsType<xAI.Protocol.ImageResponse>(response.RawRepresentation);
        Assert.NotNull(rawResponse.Model);
        output.WriteLine($"Model used: {rawResponse.Model}");
    }

    [SecretsFact("XAI_API_KEY")]
    public async Task GenerateImage_WithAspectRatioAndResolution_ReturnsImageContent()
    {
        var imageGenerator = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIImageGenerator("grok-imagine-image");

        var request = new ImageGenerationRequest("A cinematic skyline at sunrise");
        var options = new GrokImageGenerationOptions
        {
            ResponseFormat = ImageGenerationResponseFormat.Uri,
            AspectRatio = ImageAspectRatio.ImgAspectRatio169,
            Resolution = ImageResolution.ImgResolution1K,
            Count = 1
        };

        var response = await imageGenerator.GenerateAsync(request, options);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Contents);
        Assert.Single(response.Contents);

        var image = Assert.IsType<UriContent>(response.Contents.First());
        Assert.Equal("image/jpeg", image.MediaType);
        output.WriteLine($"Generated image URL: {image.Uri}");
    }

    [LocalFact("XAI_API_KEY")]
    public async Task GenerateImage_WithMultiImageEdit_ReturnsImageContent()
    {
        var imageGenerator = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIImageGenerator("grok-imagine-image");

        var seedOptions = new ImageGenerationOptions
        {
            ResponseFormat = ImageGenerationResponseFormat.Uri,
            Count = 2
        };

        var seeds = await imageGenerator.GenerateAsync(
            new ImageGenerationRequest("Two stylized character portraits"), seedOptions);

        Assert.NotNull(seeds);
        Assert.NotEmpty(seeds.Contents);
        Assert.Equal(2, seeds.Contents.Count);

        var originals = seeds.Contents.Take(2).ToList();
        var edit = await imageGenerator.GenerateAsync(
            new ImageGenerationRequest("Combine both portraits into a single movie poster", originals),
            new ImageGenerationOptions
            {
                ResponseFormat = ImageGenerationResponseFormat.Uri,
                Count = 1
            });

        Assert.NotNull(edit);
        Assert.NotEmpty(edit.Contents);
        Assert.Single(edit.Contents);
        var image = Assert.IsType<UriContent>(edit.Contents.First());
        Assert.Equal("image/jpeg", image.MediaType);
        output.WriteLine($"Edited image URL: {image.Uri}");
    }

    [Fact]
    public async Task GenerateImage_WithOneOriginalImage_SetsImageField()
    {
        GenerateImageRequest? capturedRequest = null;
        var client = new Mock<Image.ImageClient>(MockBehavior.Strict);
        client.Setup(x => x.GenerateImageAsync(It.IsAny<GenerateImageRequest>(), null, null, CancellationToken.None))
            .Callback<GenerateImageRequest, Metadata?, DateTime?, CancellationToken>((req, _, _, _) => capturedRequest = req)
            .Returns(CallHelpers.CreateAsyncUnaryCall(new ImageResponse
            {
                Images =
                {
                    new GeneratedImage { Url = "https://example.com/generated.jpg" }
                }
            }));

        var imageGenerator = client.Object.AsIImageGenerator("grok-imagine-image");
        var source = new UriContent(new Uri("https://example.com/source.jpg"), "image/jpeg");

        await imageGenerator.GenerateAsync(new ImageGenerationRequest("Edit this image", [source]));

        Assert.NotNull(capturedRequest);
        Assert.NotNull(capturedRequest.Image);
        Assert.Equal("https://example.com/source.jpg", capturedRequest.Image.ImageUrl);
        Assert.Empty(capturedRequest.Images);
    }

    [Fact]
    public async Task GenerateImage_WithMultipleOriginalImages_SetsImagesField()
    {
        GenerateImageRequest? capturedRequest = null;
        var client = new Mock<Image.ImageClient>(MockBehavior.Strict);
        client.Setup(x => x.GenerateImageAsync(It.IsAny<GenerateImageRequest>(), null, null, CancellationToken.None))
            .Callback<GenerateImageRequest, Metadata?, DateTime?, CancellationToken>((req, _, _, _) => capturedRequest = req)
            .Returns(CallHelpers.CreateAsyncUnaryCall(new ImageResponse
            {
                Images =
                {
                    new GeneratedImage { Url = "https://example.com/generated.jpg" }
                }
            }));

        var imageGenerator = client.Object.AsIImageGenerator("grok-imagine-image");
        var first = new UriContent(new Uri("https://example.com/source-1.jpg"), "image/jpeg");
        var second = new UriContent(new Uri("https://example.com/source-2.jpg"), "image/jpeg");

        await imageGenerator.GenerateAsync(new ImageGenerationRequest("Blend these images", [first, second]));

        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest.Image);
        Assert.Equal(2, capturedRequest.Images.Count);
        Assert.Equal("https://example.com/source-1.jpg", capturedRequest.Images[0].ImageUrl);
        Assert.Equal("https://example.com/source-2.jpg", capturedRequest.Images[1].ImageUrl);
    }

    [Fact]
    public async Task GenerateImage_WithAspectRatioOption_SetsProtocolAspectRatio()
    {
        GenerateImageRequest? capturedRequest = null;
        var client = new Mock<Image.ImageClient>(MockBehavior.Strict);
        client.Setup(x => x.GenerateImageAsync(It.IsAny<GenerateImageRequest>(), null, null, CancellationToken.None))
            .Callback<GenerateImageRequest, Metadata?, DateTime?, CancellationToken>((req, _, _, _) => capturedRequest = req)
            .Returns(CallHelpers.CreateAsyncUnaryCall(new ImageResponse
            {
                Images =
                {
                    new GeneratedImage { Url = "https://example.com/generated.jpg" }
                }
            }));

        var imageGenerator = client.Object.AsIImageGenerator("grok-imagine-image");

        await imageGenerator.GenerateAsync(
            new ImageGenerationRequest("Wide composition"),
            new GrokImageGenerationOptions { AspectRatio = ImageAspectRatio.ImgAspectRatio169 });

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest.HasAspectRatio);
        Assert.Equal(ImageAspectRatio.ImgAspectRatio169, capturedRequest.AspectRatio);
    }

    [Fact]
    public async Task GenerateImage_MapsProtocolUsageToResponseUsage()
    {
        var client = new Mock<Image.ImageClient>(MockBehavior.Strict);
        client.Setup(x => x.GenerateImageAsync(It.IsAny<GenerateImageRequest>(), null, null, CancellationToken.None))
            .Returns(CallHelpers.CreateAsyncUnaryCall(new ImageResponse
            {
                Images =
                {
                    new GeneratedImage { Url = "https://example.com/generated.jpg" }
                },
                Usage = new SamplingUsage
                {
                    PromptTokens = 11,
                    CompletionTokens = 7,
                    TotalTokens = 18
                }
            }));

        var imageGenerator = client.Object.AsIImageGenerator("grok-imagine-image");
        var response = await imageGenerator.GenerateAsync(new ImageGenerationRequest("Test usage mapping"));

        Assert.NotNull(response);
        Assert.NotNull(response.Usage);
        Assert.Equal(11, response.Usage.InputTokenCount);
        Assert.Equal(7, response.Usage.OutputTokenCount);
        Assert.Equal(18, response.Usage.TotalTokenCount);
    }

    [Fact]
    public async Task GenerateImage_WithNullRequest_ThrowsArgumentNullException()
    {
        var imageGenerator = new GrokClient("test-api-key")
            .AsIImageGenerator("grok-imagine-image");

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await imageGenerator.GenerateAsync(null!, null));
    }

    [Fact]
    public async Task GenerateImage_WithNullPrompt_ThrowsArgumentNullException()
    {
        var imageGenerator = new GrokClient("test-api-key")
            .AsIImageGenerator("grok-imagine-image");

        var request = new ImageGenerationRequest(null!);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await imageGenerator.GenerateAsync(request, null));
    }

    [Fact]
    public void GetService_ReturnsImageGeneratorMetadata()
    {
        var imageGenerator = new GrokClient("test-api-key")
            .AsIImageGenerator("grok-imagine-image");

        var metadata = imageGenerator.GetService<ImageGeneratorMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal("xai", metadata.ProviderName);
        Assert.Equal("grok-imagine-image", metadata.DefaultModelId);
    }
}
