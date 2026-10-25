namespace Plover.Controller.Features.Notifications;

/// <summary>A webhook that receives each notification as JSON.</summary>
/// <param name="Url">Where to post.</param>
/// <param name="Secret">When set, each request carries <c>X-Plover-Signature: sha256=…</c>, the HMAC of the body, so the receiver knows who sent it.</param>
public sealed record WebhookTarget(Uri Url, string? Secret = null);

/// <summary>A chat channel that takes an incoming webhook.</summary>
/// <param name="Url">The address of the incoming webhook.</param>
/// <param name="Format"><c>slack</c> (also Mattermost and Google Chat: <c>{"text": …}</c>) or <c>discord</c> (<c>{"content": …}</c>).</param>
public sealed record ChatTarget(Uri Url, string Format = "slack");

/// <summary>Where alert emails go.</summary>
public sealed class EmailTarget
{
    /// <summary>The SMTP server.</summary>
    public string? Host { get; set; }

    /// <summary>Its port.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether to start TLS.</summary>
    public bool Ssl { get; set; } = true;

    /// <summary>User to sign in with, or <see langword="null"/> for none.</summary>
    public string? User { get; set; }

    /// <summary>Password of that user.</summary>
    public string? Password { get; set; }

    /// <summary>The sender address.</summary>
    public string? From { get; set; }

    /// <summary>The addresses that receive the mails.</summary>
    public List<string> To { get; set; } = [];
}

/// <summary>How the controller picks its leader for sending.</summary>
public sealed class LeaderOptions
{
    /// <summary><c>none</c>: every replica sends, and the receiver relies on the stable key to de-duplicate. <c>lease</c>: one replica sends, chosen through a Kubernetes Lease, which needs all replicas in one cluster. <c>peers</c>: one replica sends, the one that ranks first among those it can reach through <c>Controller:Peers</c> (see <c>ReplicaRank</c>), which also works across data centers.</summary>
    public string Mode { get; set; } = "none";

    /// <summary>The name of the Lease.</summary>
    public string LeaseName { get; set; } = "plover-controller-alerts";

    /// <summary>The namespace of the Lease; the namespace of the controller when empty.</summary>
    public string? LeaseNamespace { get; set; }

    /// <summary>How long a leader may stay silent before another replica takes over.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>The address of the API server; found from the environment of a pod when empty.</summary>
    public string? ApiServer { get; set; }
}

/// <summary>Settings of the notifications slice.</summary>
public sealed class NotificationOptions
{
    /// <summary>The webhooks.</summary>
    public List<WebhookTarget> Webhooks { get; set; } = [];

    /// <summary>The chat channels.</summary>
    public List<ChatTarget> Chats { get; set; } = [];

    /// <summary>The email channel, used when it has a host and at least one address.</summary>
    public EmailTarget Email { get; set; } = new();

    /// <summary>How often the alerts are compared with what was sent.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Only alerts of this severity and above are sent: <c>warning</c> or <c>critical</c>.</summary>
    public string MinSeverity { get; set; } = "warning";

    /// <summary>The leader election.</summary>
    public LeaderOptions Leader { get; set; } = new();

    /// <summary>The name of this controller in what is sent, so receivers can tell environments apart; the host name when empty.</summary>
    public string? Source { get; set; }
}
