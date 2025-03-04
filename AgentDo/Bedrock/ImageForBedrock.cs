using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;

namespace AgentDo.Bedrock
{
	public class ImageForBedrock : IDisposable
	{
		public static ImageForBedrock From(FileInfo file)
		{
			var stream = new MemoryStream(File.ReadAllBytes(file.FullName));
			return new ImageForBedrock(stream, new ImageBlock
			{
				Format = file.Extension switch
				{
					".png" => ImageFormat.Png,
					".jpg" => ImageFormat.Jpeg,
					".jpeg" => ImageFormat.Jpeg,
					_ => throw new ArgumentOutOfRangeException(file.Extension)
				},
				Source = new ImageSource
				{
					Bytes = stream,
				},
			});
		}

		public Stream Stream { get; }
		public ImageBlock Image { get; }
		private ImageForBedrock(Stream stream, ImageBlock image)
		{
			Stream = stream;
			Image = image;
		}
		public void Dispose()
		{
			Stream.Dispose();
		}

		public static implicit operator ImageBlock(ImageForBedrock imageForBedrock)
		{
			return imageForBedrock.Image;
		}
	}
}
