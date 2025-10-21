using System;

public readonly struct ProfilePassScope : IDisposable
{
	private readonly string name;
	private readonly RenderGraph renderGraph;

	public ProfilePassScope(string name, RenderGraph renderGraph)
	{
		// TODO: There might be a more concise way to do this
		var pass = renderGraph.AddGenericRenderPass(name);
		pass.UseProfiler = false;

		pass.SetRenderFunction(static (command, pass, name) =>
		{
			command.BeginSample(pass.Name);
		});

		this.name = name;
		this.renderGraph = renderGraph;
	}

	readonly void IDisposable.Dispose()
	{
		// TODO: There might be a more concise way to do this
		var pass = renderGraph.AddGenericRenderPass(name);
		pass.UseProfiler = false;

		pass.SetRenderFunction(static (command, pass, name) =>
		{
			command.EndSample(pass.Name);
		});
	}
}