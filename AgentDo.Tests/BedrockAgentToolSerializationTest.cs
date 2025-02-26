﻿using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests
{
	/// <summary>
	/// Scenarios taken from https://docs.aws.amazon.com/bedrock/latest/userguide/tool-use-inference-call.html
	/// </summary>
	[TestClass]
	public sealed class BedrockAgentToolSerializationTest
	{
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
						Json = Amazon.Runtime.Documents.Document.FromObject(new
						{
							type = "object",
							properties = new Dictionary<string, object>
							{
								{ "sign", new {
									type = "string",
									description = "The call sign for the radio station for which you want the most popular song. Example calls signs are WZPZ and WKRP."
								} }
							},
							required = new string[]
							{
								"sign"
							},
						}),
					},
				}
			};

			AssertEqual(expectedTool, BedrockAgent.GetToolDefinition(Tool.From(getSongTool)));
		}

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
						Json = Amazon.Runtime.Documents.Document.FromObject(new
						{
							type = "object",
							properties = new Dictionary<string, object>
							{
								{ "song", new {
									type = "string",
									description = "The song name"
								} },
								{ "rating", new {
									type = "integer"
								} },
							},
							required = new string[]
							{
								"song",
								"rating"
							},
						}),
					},
				}
			};

			AssertEqual(expectedTool, BedrockAgent.GetToolDefinition(Tool.From(rateSongTool)));
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
						Json = Amazon.Runtime.Documents.Document.FromObject(new
						{
							type = "object",
							properties = new Dictionary<string, object>
							{
								{ "song", new {
									type = "string"
								} },
								{ "rating", new {
									type = "string"
								} },
							},
							required = new string[]
							{
								"song"
							},
						}),
					},
				}
			};

			var usableTool = Tool.From([Description("Rate a song")] (string song, string? rating) => "Rated!");

			AssertEqual(expectedTool, BedrockAgent.GetToolDefinition(usableTool));
		}

		[TestMethodWithDI]
		public async Task NestedObject(IAmazonBedrockRuntime bedrock)
		{
			var expectedTool = new Amazon.BedrockRuntime.Model.Tool
			{
				ToolSpec = new ToolSpecification
				{
					Name = "RateASong",
					Description = "Rate a song",
					InputSchema = new ToolInputSchema
					{
						Json = Amazon.Runtime.Documents.Document.FromObject(new
						{
							type = "object",
							properties = new Dictionary<string, object>
							{
								{ "song", new {
									type = "object",
									properties = new Dictionary<string, object>
									{
										{ "artist", new {
											type = "object",
											properties = new Dictionary<string, object>
											{
												{ "name", new {
													type = "string"
												} },
												{ "pseudonym", new Dictionary<string, object?> {
													{ "type", "string" },
													{ "default", "abc" },
												} },
											},
											required = new string[]
											{
												"name"
											},
										} },
										{ "name", new {
											type = "string"
										} },
										{ "length", new {
											type = "number"
										} },
									},
									required = new string[]
									{
										"artist",
										"name",
										"length"
									},
								} },
								{ "rating", new {
									type = "integer"
								} },
							},
							required = new string[]
							{
								"song",
								"rating"
							},
						}),
					},
				}
			};

			SongRating? toolCall = null;
			var usableTool = Tool.From([Description("Rate a song")] (Song song, int rating) =>
			{
				toolCall = new SongRating(song, rating);
				return "Rated!";
			});
			var bedrockTool = BedrockAgent.GetToolDefinition(usableTool);
			AssertEqual(expectedTool, bedrockTool);

			//Actually processing it as json
			var response = await bedrock.ConverseWithTool("I would like to rate the sone All Too Well by Taylor Swift with 1.", bedrockTool);
			var toolUse = response.Output.Message.Content[1].ToolUse;
			var songRating = toolUse.Input.FromAmazonJson<SongRating>()!;
			Assert.AreEqual("Taylor Swift", songRating.Song.Artist.Name);
			Assert.AreEqual("abc", songRating.Song.Artist.Pseudonym);
			Assert.AreEqual("All Too Well", songRating.Song.Name);
			Assert.AreEqual(1, songRating.Rating);

			//Actually processing it though the bedrock agent tool
			var toolResult = await BedrockAgent.Use([usableTool], toolUse, ConversationRole.Assistant, null);
			Assert.AreEqual("Taylor Swift", toolCall!.Song.Artist.Name);
			Assert.AreEqual("abc", toolCall!.Song.Artist.Pseudonym);
			Assert.AreEqual("All Too Well", toolCall!.Song.Name);
			Assert.AreEqual(1, toolCall!.Rating);
		}
		record Song(Artist Artist, string Name, decimal Length);
		record Artist(string Name, string Pseudonym = "abc");
		record SongRating(Song Song, int Rating);

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
			var bedrockTool = BedrockAgent.GetToolDefinition(usableTool);

			//Actually processing it as json
			var response = await bedrock.ConverseWithTool("Here is the Album RED from Taylor Swift. It was already bought three times.", bedrockTool);
			var toolUse = response.Output.Message.Content[1].ToolUse;
			var recognized = toolUse.Input.FromAmazonJson<RecognizedAlbum>()!;
			Console.WriteLine(JsonSerializer.Serialize(recognized));
			Assert.AreEqual("Taylor Swift", recognized.Album.Artist.Name);
			Assert.AreEqual("RED", recognized.Album.Name);
			Assert.IsNull(recognized.Album.CustomAlias);

			//Actually processing it though the bedrock agent tool
			var toolResult = await BedrockAgent.Use([usableTool], toolUse, ConversationRole.Assistant, null);
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
