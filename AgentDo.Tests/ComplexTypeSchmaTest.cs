using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

namespace AgentDo.Tests
{
	[TestClass]
	public sealed class ComplexTypeSchmaTest
	{
		class AnalyzedRequirementPlan
		{
			[Description("Short name of the requirement.")]
			public string Title { get; set; } = null!;
			public string? Summary { get; set; }

			[Description("With what meaningful and valuable milestones can we interatively and incrementally reach the realization of the requirement?")]
			public Milestone[] Milestones { get; set; } = null!;

			public class Milestone
			{
				[Description("Short name of the milestone.")]
				public string Title { get; set; } = null!;
				public string? Description { get; set; }

				[Description("What do we have to do before implementation, to ensure the implementation will succeed?")]
				public Activity[] Preparation { get; set; } = null!;

				[Description("What do we actually have to implement to reach the next milestone?")]
				public Activity[] Implementation { get; set; } = null!;

				[Description("How can we make sure what we have implemented actually works and gets us to the next milestone?")]
				public Activity[] Testing { get; set; } = null!;

				public class Activity
				{
					[Description("Short name of the activity.")]
					public string Title { get; set; } = null!;
					public string? Description { get; set; }
				}
			}
		}

		[TestMethod]
		public void ComplexTypeSchemaDoesNotUseRef()
		{
			var autoSchema = JsonSchemaExtensions.JsonSchemaString<AnalyzedRequirementPlan>();
			Console.WriteLine(autoSchema);

			Assert.IsTrue(!autoSchema.Contains("$ref"));
		}
	}
}
