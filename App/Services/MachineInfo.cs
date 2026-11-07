using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Mnet.App.Services;

public static class MachineInfo
{
    private const string PasswordAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    public static string LocalAddress { get; } = FindLocalAddress();

    public static string NewPassword()
    {
        var characters = new char[8];
        for (var i = 0; i < characters.Length; i++)
        {
            characters[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        }

        return new string(characters);
    }

    private static string FindLocalAddress()
    {
        try
        {
            var candidates = new List<(string Address, bool HasGateway)>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211) ||
                    IsVirtualAdapter(adapter))
                {
                    continue;
                }

                var properties = adapter.GetIPProperties();
                var hasGateway = properties.GatewayAddresses
                    .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

                foreach (var unicast in properties.UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                    {
                        continue;
                    }

                    var text = address.ToString();
                    if (text.StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    candidates.Add((text, hasGateway));
                }
            }

            var best = candidates
                .OrderByDescending(c => c.HasGateway)
                .ThenByDescending(c => c.Address.StartsWith("192.168.", StringComparison.Ordinal))
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(best.Address))
            {
                return best.Address;
            }
        }
        catch (NetworkInformationException)
        {
        }

        return "127.0.0.1";
    }

    private static bool IsVirtualAdapter(NetworkInterface adapter)
    {
        var description = (adapter.Description + " " + adapter.Name).ToLowerInvariant();
        return description.Contains("virtual") || description.Contains("vmware") ||
               description.Contains("hyper-v") || description.Contains("vethernet") ||
               description.Contains("tap-") || description.Contains("vbox");
    }
}
