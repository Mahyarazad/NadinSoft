﻿using FluentResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NadinfSoft.Identity.Models;
using NadinSoft.Application.Abstractions.Identity;
using NadinSoft.Application.Features.Identity.Commands.Login;
using NadinSoft.Application.Features.Identity.Commands.Register;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace NadinfSoft.Identity.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly JwtSettings _jwtSettings;
        public AuthService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IOptions<JwtSettings> jwtSettings)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _jwtSettings = jwtSettings.Value;
        }

        public async Task<Result<LoginCommandResult>> Login(LoginCommand command, CancellationToken cancellationToken = default)
        {
            var userByEmail = await _userManager.FindByEmailAsync(command.EmailOrUser);
            var userByUsername = await _userManager.FindByNameAsync(command.EmailOrUser);
            if(userByEmail == null && userByUsername == null)
            {
                return Result.Fail("There is no registered username or Email.");
            }

            var user = userByEmail != null ? userByEmail : userByUsername;

            var singInResult = await _signInManager.CheckPasswordSignInAsync(user!, command.Password, true);

            if(singInResult.IsLockedOut)
            {
                return Result.Fail("Account Locked, too many invalid login attempts.");
            }

            if(singInResult.Succeeded)
            {
                var token = await GenerateJWTToken(user);

                return Result.Ok(new LoginCommandResult(user.Id, user.UserName, user.Email, new JwtSecurityTokenHandler().WriteToken(token)));
            }

            return Result.Fail("Invalid password.");
        }

        public async Task<Result<RegisterCommandResult>> Register(RegisterCommand command, CancellationToken cancellationToken = default)
        {
            var existingUser = await _userManager.FindByNameAsync(command.UserName);
            var existingEmail = await _userManager.FindByEmailAsync(command.Email);
            if (existingUser != null)
            {
                return Result.Fail(new Error($"'{existingUser.UserName}' already exists."));
            }

            if(existingEmail != null)
            {
                return Result.Fail(new Error($"'{existingEmail.Email}' already exists."));
            }

            var user = new ApplicationUser()
            {
                Email = command.Email,
                UserName = command.UserName,
                Firstname = command.FirstName,
                Lastname = command.LastName,
                EmailConfirmed = true,
            };
            var identityResult = await _userManager.CreateAsync(user, command.Password);

            if(identityResult.Succeeded)
            {
                return Result.Ok(new RegisterCommandResult(user.Id));
            }

            return Result.Fail(new Error(string.Join(", ", identityResult.Errors)));

        }

        private async Task<JwtSecurityToken> GenerateJWTToken(ApplicationUser user)
        {
            var userClaims = await _userManager.GetClaimsAsync(user);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                // I dont like the log ClaimType.GivenName in the token like this => http://schemas.microsoft.com/ws/2008/06/identity/claims/userdata
                new Claim("Uid", user.Id),
                new Claim("Name", user.Firstname),
                new Claim("Role", "User"),
           }.Union(userClaims);

            var symmetricSecurityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings!.Secret));
            var signingCredentials = new SigningCredentials(symmetricSecurityKey, SecurityAlgorithms.HmacSha256);

            var jwtSecurityToken =
                new JwtSecurityToken(
                    issuer: _jwtSettings!.Issuer,
                    audience: _jwtSettings!.Audience,
                    claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(30),
                    signingCredentials: signingCredentials
                );
            return jwtSecurityToken;
        }
    }
}
