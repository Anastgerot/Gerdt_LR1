using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Gerdt_LR1.Auth
{
    public static class AuthOptions
    {
        public const string ISSUER = "GerdtAuth";
        public const string AUDIENCE = "GerdtClient";
        public const int LIFETIME = 10;

        private const string KEY = "super_secret_key_12345_super_secret_key_12345_super_secret_key_12345";
        public static SymmetricSecurityKey GetSymmetricSecurityKey()
        {
            return new SymmetricSecurityKey(Encoding.ASCII.GetBytes(KEY));
        }

        public static object GenerateToken(bool isAdmin = false, string? login = "guest")
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, login ?? "guest"),
                new Claim(ClaimTypes.Role, isAdmin ? "admin" : "user")
            };

            var now = DateTime.UtcNow;
            var jwt = new JwtSecurityToken(
                issuer: ISSUER,
                audience: AUDIENCE,
                notBefore: now,
                expires: now.AddMinutes(LIFETIME),
                claims: claims,
                signingCredentials: new SigningCredentials(GetSymmetricSecurityKey(), SecurityAlgorithms.HmacSha256)
            );

            var token = new JwtSecurityTokenHandler().WriteToken(jwt);
            return new { access_token = token, username = login ?? "guest", role = isAdmin ? "admin" : "user", expires = jwt.ValidTo };
        }

    }
}
