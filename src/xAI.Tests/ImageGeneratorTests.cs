using Microsoft.Extensions.AI;
using Tests.Client.Helpers;
using xAI;
using static ConfigurationExtensions;

namespace xAI.Tests;

public class ImageGeneratorTests(ITestOutputHelper output)
{
    [SecretsFact("XAI_API_KEY")]
    public async Task GenerateImage_WithPrompt_ReturnsImageContent()
    {
        var imageGenerator = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIImageGenerator("grok-2-image");

        var request = new ImageGenerationRequest("A cat sitting on a tree branch");
        var options = new ImageGenerationOptions
        {
            ResponseFormat = ImageGenerationResponseFormat.Uri,
            Count = 1
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
    public async Task GenerateImage_WithBase64Response_ReturnsDataContent()
    {
        var imageGenerator = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIImageGenerator("grok-2-image");

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
    public async Task GenerateMultipleImages_ReturnsCorrectCount()
    {
        var imageGenerator = new GrokClient(Configuration["XAI_API_KEY"]!)
            .AsIImageGenerator("grok-2-image");

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
            .AsIImageGenerator("grok-2-image");

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

    [Fact]
    public async Task GenerateImage_WithNullRequest_ThrowsArgumentNullException()
    {
        var imageGenerator = new GrokClient("test-api-key")
            .AsIImageGenerator("grok-2-image");

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await imageGenerator.GenerateAsync(null!, null));
    }

    [Fact]
    public async Task GenerateImage_WithNullPrompt_ThrowsArgumentNullException()
    {
        var imageGenerator = new GrokClient("test-api-key")
            .AsIImageGenerator("grok-2-image");

        var request = new ImageGenerationRequest(null!);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await imageGenerator.GenerateAsync(request, null));
    }

    [Fact]
    public void GetService_ReturnsImageGeneratorMetadata()
    {
        var imageGenerator = new GrokClient("test-api-key")
            .AsIImageGenerator("grok-2-image");

        var metadata = imageGenerator.GetService<ImageGeneratorMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal("xai", metadata.ProviderName);
        Assert.Equal("grok-2-image", metadata.DefaultModelId);
    }
}
