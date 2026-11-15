using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace MonopolyBank.E2E;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class AppTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new() { Width = 390, Height = 844 },
        Locale = "en-US",
        HasTouch = true,
    };

    private static string Url(string path = "") => AppServer.BaseUrl + path;

    private async Task OpenApp(IPage page, string path = "")
    {
        await page.GotoAsync(Url(path));
        await page.Locator(".topbar").WaitForAsync(new() { Timeout = 60_000 });
    }

    private static ILocator Balance(IPage page, string player) =>
        page.Locator($"[data-player='{player}'] [data-testid=balance]");

    [Test]
    public async Task SharedBank_PayUndoAndPersist()
    {
        await OpenApp(Page);
        await Page.GetByTestId("mode-shared").ClickAsync();
        var names = Page.GetByTestId("player-name");
        await names.Nth(0).FillAsync("Anna");
        await names.Nth(1).FillAsync("Ben");
        await Page.GetByTestId("start-game").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/bank$"));
        await Expect(Balance(Page, "Anna")).ToHaveTextAsync("M1,500");

        // Anna passes GO
        await Page.Locator("[data-player=Anna] [data-testid=go]").ClickAsync();
        await Expect(Balance(Page, "Anna")).ToHaveTextAsync("M1,700");

        // Anna pays Ben 250 on the keypad (card-style, no banknotes)
        await Page.Locator("[data-player=Anna] [data-testid=pay]").ClickAsync();
        await Page.GetByTestId("to").GetByRole(AriaRole.Button, new() { Name = "Ben" }).ClickAsync();
        await Expect(Page.Locator(".bill")).ToHaveCountAsync(0);
        foreach (var d in "250") await Page.GetByTestId($"key-{d}").ClickAsync();
        await Expect(Page.GetByTestId("pad-amount")).ToContainTextAsync("250");
        await Page.GetByTestId("confirm-pay").ClickAsync();
        await Expect(Balance(Page, "Anna")).ToHaveTextAsync("M1,450");
        await Expect(Balance(Page, "Ben")).ToHaveTextAsync("M1,750");

        // Undo from the toast
        await Page.GetByTestId("toast").GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();
        await Expect(Balance(Page, "Anna")).ToHaveTextAsync("M1,700");

        // Survives a reload (localStorage)
        await Page.ReloadAsync();
        await Expect(Balance(Page, "Anna")).ToHaveTextAsync("M1,700", new() { Timeout = 60_000 });

        // Overspending is refused
        await Page.Locator("[data-player=Ben] [data-testid=pay]").ClickAsync();
        await Page.GetByTestId("to").GetByRole(AriaRole.Button, new() { Name = "Bank" }).ClickAsync();
        foreach (var d in "9999") await Page.GetByTestId($"key-{d}").ClickAsync();
        await Page.GetByTestId("confirm-pay").ClickAsync();
        await Expect(Page.GetByTestId("toast").Last).ToHaveTextAsync("Not enough money!");
        await Expect(Balance(Page, "Ben")).ToHaveTextAsync("M1,500");
    }

    [Test]
    public async Task Wallet_JoinAndTransferBetweenTwoPhones()
    {
        await OpenApp(Page, "new/wallet");
        await Page.GetByTestId("your-name").FillAsync("Host");
        await Page.GetByTestId("create-wallet").ClickAsync();
        var gameCode = await Page.GetByTestId("invite-code").InnerTextAsync();
        await Page.Keyboard.PressAsync("Escape");

        // Second phone = separate browser context (own localStorage)
        await using var guestCtx = await Browser.NewContextAsync(ContextOptions());
        var guest = await guestCtx.NewPageAsync();
        await OpenApp(guest, "join");
        // Typed without dashes, in lower case: the field formats it as XXXX-XXXX-XXXX-XXXX.
        await guest.GetByTestId("game-code").PressSequentiallyAsync(gameCode.Replace("-", "").ToLowerInvariant());
        await Expect(guest.GetByTestId("game-code")).ToHaveValueAsync(gameCode);
        await guest.GetByTestId("use-code").ClickAsync();
        await Expect(guest.GetByTestId("join-rules")).ToContainTextAsync("1,500");
        await guest.GetByTestId("your-name").FillAsync("Guest");
        await guest.GetByTestId("join-game").ClickAsync();
        await Expect(guest.GetByTestId("balance")).ToHaveTextAsync("M1,500");

        // Host pays 300
        await Page.GetByTestId("send").ClickAsync();
        foreach (var d in "300") await Page.GetByTestId($"key-{d}").ClickAsync();
        await Page.GetByTestId("create-transfer").ClickAsync();
        var code = await Page.GetByTestId("sent-code").InnerTextAsync();
        await Expect(Page.Locator("img.qr")).ToBeVisibleAsync();
        await Page.GetByTestId("transfer-done").ClickAsync();
        await Expect(Page.GetByTestId("balance")).ToHaveTextAsync("M1,200");

        // Guest receives by typing the code
        await guest.GetByTestId("receive").ClickAsync();
        await guest.GetByTestId("transfer-code").PressSequentiallyAsync(code.Replace("-", ""));
        await Expect(guest.GetByTestId("transfer-code")).ToHaveValueAsync(code);
        await guest.GetByTestId("confirm-receive").ClickAsync();
        await Expect(guest.GetByTestId("balance")).ToHaveTextAsync("M1,800");

        // Same code again is rejected
        await guest.GetByTestId("receive").ClickAsync();
        await guest.GetByTestId("transfer-code").FillAsync(code);
        await guest.GetByTestId("confirm-receive").ClickAsync();
        await Expect(guest.GetByTestId("toast").Last).ToHaveTextAsync("This payment was already received.");
        await Expect(guest.GetByTestId("balance")).ToHaveTextAsync("M1,800");
    }

    [Test]
    public async Task Edition_TravelsWithInviteLink_AndCanBeSaved()
    {
        // Host picks the Euro edition, changes the starting money and saves it as an own edition.
        await OpenApp(Page, "new/wallet");
        await Page.GetByTestId("edition-euro").ClickAsync();
        await Page.GetByTestId("start-money").FillAsync("2000");
        await Page.GetByTestId("save-edition-open").ClickAsync();
        await Page.GetByTestId("edition-name").FillAsync("Our box");
        await Page.GetByTestId("save-edition").ClickAsync();
        await Expect(Page.Locator(".edition.selected")).ToContainTextAsync("Our box");
        await Page.GetByTestId("your-name").FillAsync("Host");
        await Page.GetByTestId("create-wallet").ClickAsync();
        await Expect(Page.GetByTestId("balance")).ToHaveTextAsync("2,000 €");
        var joinUrl = await Page.Locator("img.qr").GetAttributeAsync("data-qr");
        Assert.That(joinUrl, Does.Contain("/join?g="));

        // Saved edition is offered again next time.
        await Page.GotoAsync(Url("new/shared"));
        await Expect(Page.Locator(".edition", new() { HasText = "Our box" })).ToBeVisibleAsync(new() { Timeout = 60_000 });

        // Guest opens the invite link: the currency comes along.
        await using var guestCtx = await Browser.NewContextAsync(ContextOptions());
        var guest = await guestCtx.NewPageAsync();
        await guest.GotoAsync(joinUrl!);
        await Expect(guest.GetByTestId("join-rules")).ToContainTextAsync("2,000 €", new() { Timeout = 60_000 });
        await guest.GetByTestId("your-name").FillAsync("Guest");
        await guest.GetByTestId("join-game").ClickAsync();
        await Expect(guest.GetByTestId("balance")).ToHaveTextAsync("2,000 €");
        await guest.GetByTestId("from-bank").ClickAsync();
        await guest.GetByTestId("key-1").ClickAsync();
        await guest.GetByTestId("key-000").ClickAsync();
        await Expect(guest.GetByTestId("pad-amount")).ToContainTextAsync("1,000 €");
    }

    [Test]
    public async Task MillionsEdition_HandlesBigAmounts()
    {
        await OpenApp(Page, "new/shared");
        await Page.GetByTestId("edition-millions").ClickAsync();
        var names = Page.GetByTestId("player-name");
        await names.Nth(0).FillAsync("Anna");
        await names.Nth(1).FillAsync("Ben");
        await Page.GetByTestId("start-game").ClickAsync();
        await Expect(Balance(Page, "Anna")).ToHaveTextAsync("M15,000,000");
        await Page.Locator("[data-player=Anna] [data-testid=pay]").ClickAsync();
        await Page.GetByTestId("to").GetByRole(AriaRole.Button, new() { Name = "Ben" }).ClickAsync();
        foreach (var d in "5500") await Page.GetByTestId($"key-{d}").ClickAsync();
        await Page.GetByTestId("key-000").ClickAsync();
        await Page.GetByTestId("confirm-pay").ClickAsync();
        await Expect(Balance(Page, "Ben")).ToHaveTextAsync("M20,500,000");
    }

    [Test]
    public async Task EmptyNames_GetLocalizedDefaults()
    {
        await OpenApp(Page);
        await Page.GetByTestId("lang-cs").ClickAsync();
        await Page.GetByTestId("mode-shared").ClickAsync();
        await Page.GetByTestId("add-player").ClickAsync();
        await Page.GetByTestId("player-name").Nth(1).FillAsync("Eva");
        await Page.GetByTestId("start-game").ClickAsync();
        await Expect(Page.GetByTestId("player-card")).ToHaveCountAsync(3);
        await Expect(Balance(Page, "Hráč 1")).ToBeVisibleAsync();
        await Expect(Balance(Page, "Eva")).ToBeVisibleAsync();
        await Expect(Balance(Page, "Hráč 3")).ToBeVisibleAsync();

        await Page.GotoAsync(Url("new/wallet"));
        await Expect(Page.GetByTestId("your-name")).ToHaveAttributeAsync("placeholder", "Hráč 1", new() { Timeout = 60_000 });
        await Page.GetByTestId("create-wallet").ClickAsync();
        await Expect(Page.GetByTestId("player-card")).ToHaveAttributeAsync("data-player", "Hráč 1");
    }

    [Test]
    public async Task Language_SwitchesAndPersists()
    {
        await OpenApp(Page);
        await Page.GetByTestId("lang-de").ClickAsync();
        await Expect(Page.GetByTestId("mode-shared")).ToContainTextAsync("Eine gemeinsame Bank");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "de");
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("mode-shared")).ToContainTextAsync("Eine gemeinsame Bank", new() { Timeout = 60_000 });
        await Page.GetByTestId("lang-cs").ClickAsync();
        await Expect(Page.GetByTestId("mode-shared")).ToContainTextAsync("Jedna společná banka");
    }

    [Test]
    public async Task Pwa_WorksOffline_AndPingsWithoutData()
    {
        if (!AppServer.IsLocal) Assert.Ignore("Offline/ping check needs the local test server.");

        await OpenApp(Page);
        // Wait until the published service worker has cached everything and controls the page.
        await Page.EvaluateAsync("navigator.serviceWorker.ready");
        await Page.ReloadAsync();
        await Page.WaitForFunctionAsync("!!navigator.serviceWorker.controller", null, new() { Timeout = 60_000 });

        var manifest = await Page.Locator("link[rel=manifest]").GetAttributeAsync("href");
        Assert.That(manifest, Is.EqualTo("manifest.webmanifest"));

        for (var i = 0; i < 50 && AppServer.Pings == 0; i++) await Task.Delay(100);
        Assert.That(AppServer.Pings, Is.GreaterThanOrEqualTo(1), "App should ping on start");
        Assert.That(AppServer.LastPingBodyLength, Is.EqualTo(0), "Ping must not carry any data");

        await Context.SetOfflineAsync(true);
        await Page.GotoAsync(Url("new/shared"));
        await Expect(Page.GetByTestId("start-game")).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await Context.SetOfflineAsync(false);
    }
}
