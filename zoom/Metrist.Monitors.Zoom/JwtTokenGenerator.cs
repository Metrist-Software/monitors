using System;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Metrist.Monitors.Zoom
{
	public class JwtTokenGenerator
	{
		public static string GenerateToken(string apiKey, string apiSecret)
		{
			DateTime expiry = DateTime.UtcNow.AddMinutes(2);
			int ts = (int)(expiry - new DateTime(1970, 1, 1)).TotalSeconds;
			var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(apiSecret));
			var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
			var header = new JwtHeader(credentials);
			var payload = new JwtPayload
			{
				{ "iss", apiKey },
				{ "exp", ts },
			};
			var securityToken = new JwtSecurityToken(header, payload);
			var handler = new JwtSecurityTokenHandler();

			return handler.WriteToken(securityToken);
		}
	}
}
