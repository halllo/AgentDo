using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Net.Http.Headers;

public class SetAccessToken : DelegatingHandler
{
	private readonly IHttpContextAccessor httpContextAccessor;

	public SetAccessToken(IHttpContextAccessor httpContextAccessor)
	{
		this.httpContextAccessor = httpContextAccessor;
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var http = this.httpContextAccessor.HttpContext ?? throw new InvalidOperationException("Authentication needed");
		var accessToken = await http.GetTokenAsync(OpenIdConnectParameterNames.AccessToken);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
		return await base.SendAsync(request, cancellationToken);
	}
}