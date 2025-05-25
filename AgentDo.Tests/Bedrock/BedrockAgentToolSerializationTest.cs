using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests.Bedrock
{
	/// <summary>
	/// Scenarios taken from https://docs.aws.amazon.com/bedrock/latest/userguide/tool-use-inference-call.html
	/// </summary>
	[TestClass]
	public sealed class BedrockAgentToolSerializationTest
	{
		record GetRadioSongTool(
			[property: Description("The call sign for the radio station for which you want the most popular song. Example calls signs are WZPZ and WKRP.")]
			string Sign);

		[TestMethod]
		public void StringToString()
		{
			var getSongTool = [Description("Gets the current song on the radio")]
			(
				[Description("The call sign for the radio station for which you want the most popular song. Example calls signs are WZPZ and WKRP."), Required] string sign
			) => "Random Song 1";

			var expectedTool = new Amazon.BedrockRuntime.Model.Tool
			{
				ToolSpec = new ToolSpecification
				{
					Name = "getSongTool",
					Description = "Gets the current song on the radio",
					InputSchema = new ToolInputSchema
					{
						Json = typeof(GetRadioSongTool).ToJsonSchema().ToAmazonJson(),
					},
				}
			};

			AssertEqual(expectedTool, Tool.From(getSongTool).AsBedrockTool());
		}


		record RateSongTool(
			[property: Description("The song name")] string Song,
			int Rating
			);

		[TestMethod]
		public void StringAndIntToString()
		{
			var rateSongTool = [Description("Rate a song")]
			(
				[Description("The song name"), Required] string song,
				[Required] int rating
			) => "Rated!";

			var expectedTool = new Amazon.BedrockRuntime.Model.Tool
			{
				ToolSpec = new ToolSpecification
				{
					Name = "rateSongTool",
					Description = "Rate a song",
					InputSchema = new ToolInputSchema
					{
						Json = typeof(RateSongTool).ToJsonSchema().ToAmazonJson(),
					},
				}
			};

			AssertEqual(expectedTool, Tool.From(rateSongTool).AsBedrockTool());
		}

		[TestMethod]
		public void InlineMethod()
		{
			var expectedTool = new Amazon.BedrockRuntime.Model.Tool
			{
				ToolSpec = new ToolSpecification
				{
					Name = "RateASong",
					Description = "Rate a song",
					InputSchema = new ToolInputSchema
					{
						Json = typeof(RateSongTool).ToJsonSchema().ToAmazonJson(),
					},
				}
			};

			var usableTool = Tool.From([Description("Rate a song")] (
				[Description("The song name")] string song,
				int rating
			) => "Rated!");

			AssertEqual(expectedTool, usableTool.AsBedrockTool());
		}


		record RateSongToolWithNestedObjects(Song Song, int Rating);
		record Song(Artist Artist, string Name, decimal Length);
		record Artist(string Name, string Pseudonym = "abc");

		[TestMethodWithDI]
		public async Task NestedObjects(IAmazonBedrockRuntime bedrock)
		{
			var expectedTool = new Amazon.BedrockRuntime.Model.Tool
			{
				ToolSpec = new ToolSpecification
				{
					Name = "RateASong",
					Description = "Rate a song",
					InputSchema = new ToolInputSchema
					{
						Json = typeof(RateSongToolWithNestedObjects).ToJsonSchema().ToAmazonJson(),
					},
				}
			};

			RateSongToolWithNestedObjects? toolCall = null;
			var usableTool = Tool.From([Description("Rate a song")] (Song song, int rating) =>
			{
				toolCall = new RateSongToolWithNestedObjects(song, rating);
				return "Rated!";
			});
			var bedrockTool = usableTool.AsBedrockTool();
			AssertEqual(expectedTool, bedrockTool);

			//Actually processing it as json
			var response = await bedrock.ConverseWithTool("I would like to rate the sone All Too Well by Taylor Swift with 1.", bedrockTool);
			var toolUse = response.Output.Message.Content[1].ToolUse;
			var songRating = toolUse.Input.FromAmazonJson<RateSongToolWithNestedObjects>()!;
			Assert.AreEqual("Taylor Swift", songRating.Song.Artist.Name);
			Assert.AreEqual("abc", songRating.Song.Artist.Pseudonym);
			Assert.AreEqual("All Too Well", songRating.Song.Name);
			Assert.AreEqual(1, songRating.Rating);

			//Actually processing it though the bedrock agent tool
			var toolResult = await usableTool.UseAsBedrockTool(toolUse, ConversationRole.Assistant);
			Assert.IsNotNull(toolResult.Item1);
			Assert.AreEqual("Taylor Swift", toolCall!.Song.Artist.Name);
			Assert.AreEqual("abc", toolCall!.Song.Artist.Pseudonym);
			Assert.AreEqual("All Too Well", toolCall!.Song.Name);
			Assert.AreEqual(1, toolCall!.Rating);
		}

		private static void AssertEqual(Amazon.BedrockRuntime.Model.Tool expected, Amazon.BedrockRuntime.Model.Tool actual)
		{
			Assert.AreEqual(expected.ToolSpec.Name, actual.ToolSpec.Name, "'name' mismatch");
			Assert.AreEqual(expected.ToolSpec.Description, actual.ToolSpec.Description, "'description' mismatch");

			Assert.AreEqual(
				expected: expected.ToolSpec.InputSchema.Json.FromAmazonJson(),
				actual: actual.ToolSpec.InputSchema.Json.FromAmazonJson(),
				message: "'inputSchema' mismatch");
		}

		[TestMethodWithDI]
		public async Task PrimitivesAndNestedObjectWithNullableProperty(IAmazonBedrockRuntime bedrock)
		{
			RecognizedAlbum? toolCall = null;
			var usableTool = Tool.From([Description("Detect Album")] (Album album, decimal price, string clerk, bool inStock, int purchaseCounter) =>
			{
				toolCall = new RecognizedAlbum(album, price, clerk, inStock, purchaseCounter);
				return "";
			});
			var bedrockTool = usableTool.AsBedrockTool();

			//Actually processing it as json
			var response = await bedrock.ConverseWithTool("Here is the Album RED from Taylor Swift. It was already bought three times.", bedrockTool);
			var toolUse = response.Output.Message.Content[1].ToolUse;
			var recognized = toolUse.Input.FromAmazonJson<RecognizedAlbum>()!;
			Console.WriteLine(JsonSerializer.Serialize(recognized));
			Assert.AreEqual("Taylor Swift", recognized.Album.Artist.Name);
			Assert.AreEqual("RED", recognized.Album.Name);
			Assert.IsNull(recognized.Album.CustomAlias);

			//Actually processing it though the bedrock agent tool
			var toolResult = await usableTool.UseAsBedrockTool(toolUse, ConversationRole.Assistant);
			Assert.IsNotNull(toolResult.Item1);
			Assert.AreEqual("Taylor Swift", toolCall!.Album.Artist.Name);
			Assert.AreEqual("RED", toolCall!.Album.Name);
			Assert.IsNull(toolCall!.Album.CustomAlias);
			Assert.AreEqual(recognized.Price, toolCall!.Price);
			Assert.AreEqual(recognized.Clerk, toolCall!.Clerk);
			Assert.AreEqual(recognized.InStock, toolCall!.InStock);
			Assert.AreEqual(recognized.PurchaseCounter, toolCall!.PurchaseCounter);
		}
		record Album(Artist Artist, string Name, string? CustomAlias = null);
		record RecognizedAlbum(Album Album, decimal Price, string Clerk, bool InStock, int PurchaseCounter);
	}
}
