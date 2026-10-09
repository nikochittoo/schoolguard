using System.Security.Cryptography;
using System.Text.Json;
using SchoolGuard.Common;

namespace SchoolGuard.Agent;

public class AuthService
{
    public record User(string Name, string Salt, string Hash);

    readonly object _l = new();
    readonly List<User> _users = new();
    readonly Dictionary<string, DateTime> _sessions = new();
    readonly Dictionary<string, (int Fails, DateTime Until)> _fails = new();

    public AuthService()
    {
        if (File.Exists(AppPaths.UsersFile))
            _users = JsonSerializer.Deserialize<List<User>>(File.ReadAllText(AppPaths.UsersFile)) ?? new();
    }

    public bool HasUsers { get { lock (_l) return _users.Count > 0; } }

    static string Hash(string pw, byte[] salt) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(pw, salt, 200_000, HashAlgorithmName.SHA256, 32));

    public bool CreateUser(string name, string password)
    {
        if (string.IsNullOrWhiteSpace(name) || password is null || password.Length < 8) return false;
        lock (_l)
        {
            if (_users.Any(u => u.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))) return false;
            var salt = RandomNumberGenerator.GetBytes(16);
            _users.Add(new User(name.Trim(), Convert.ToBase64String(salt), Hash(password, salt)));
            File.WriteAllText(AppPaths.UsersFile, JsonSerializer.Serialize(_users));
            return true;
        }
    }

    public string? Login(string name, string password)
    {
        name = (name ?? "").Trim();
        var key = name.ToLowerInvariant();
        lock (_l)
        {
            _fails.TryGetValue(key, out var f);
            if (f.Until > DateTime.UtcNow) return null; // bloqueado temporalmente

            var u = _users.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (u != null && password != null)
            {
                var expected = Convert.FromBase64String(u.Hash);
                var actual = Convert.FromBase64String(Hash(password, Convert.FromBase64String(u.Salt)));
                if (CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    _fails.Remove(key);
                    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    _sessions[token] = DateTime.UtcNow.AddHours(8);
                    return token;
                }
            }
            var n = f.Fails + 1;
            _fails[key] = (n, n >= 5 ? DateTime.UtcNow.AddSeconds(60) : DateTime.MinValue);
            return null;
        }
    }

    public bool Valid(string token)
    {
        lock (_l) return _sessions.TryGetValue(token, out var exp) && exp > DateTime.UtcNow;
    }
}
