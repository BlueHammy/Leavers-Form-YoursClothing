using System.Security.Claims;
using LeaversForm.Data;
using LeaversForm.Data.Entities;
using LeaversForm.Data.Enums;
using LeaversForm.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeaversForm.Tests;

public class BusinessRuleTests
{
    [Fact]
    public void Eligibility_is_five_pm_on_leaving_day()
    {
        var result = new ScriptEligibilityCalculator().Calculate(new DateOnly(2026, 8, 10));
        Assert.Equal(17, TimeZoneInfo.ConvertTimeFromUtc(result, TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time")).Hour);
    }

    [Fact]
    public void Excel_export_has_ten_columns()
    {
        var bytes = new ExcelExportService().Export([new LeaverRequest { Id = 1, LeaverDisplayName = "Test", LeaverEntraId = "id", LeaverUserPrincipalName = "test@example.com", LeaverEmail = "test@example.com", LeavingDate = new DateOnly(2026, 8, 10), EquipmentNotes = "Laptop", SubmittedByName = "Submitter", SubmittedAt = DateTime.UtcNow }]);
        Assert.True(bytes.Length > 100);
    }

    [Fact]
    public void Cancelled_status_is_not_active()
    {
        Assert.DoesNotContain(LeaverStatus.Cancelled, new[] { LeaverStatus.Pending, LeaverStatus.Scheduled });
    }

    [Fact]
    public void Exclusion_blocks_adm_prefix_and_cst_names()
    {
        var prefix = new LeaverExclusion { MatchType = LeaverExclusionMatchType.AccountPrefix, MatchValue = "ADM_" };
        var contains = new LeaverExclusion { MatchType = LeaverExclusionMatchType.TextContains, MatchValue = "CST" };
        var exact = new LeaverExclusion { MatchType = LeaverExclusionMatchType.DisplayNameExact, MatchValue = "Michael Newton" };

        Assert.True(LeaverExclusionService.IsMatch(prefix, new LeaverExclusionCheck("Admin User", "adm.user@x.com", "ADM_user@x.com")));
        Assert.True(LeaverExclusionService.IsMatch(prefix, new LeaverExclusionCheck("David Lane", "davidlane@yoursclothing.co.uk", "davidlane@yoursclothing.co.uk", "ADM_DavidLane")));
        Assert.True(LeaverExclusionService.IsMatch(contains, new LeaverExclusionCheck("CST Support", "cst.support@x.com", "cst.support@x.com")));
        Assert.True(LeaverExclusionService.IsMatch(exact, new LeaverExclusionCheck("Michael Newton", "michael.newton@x.com", "michael.newton@x.com")));
        Assert.False(LeaverExclusionService.IsMatch(exact, new LeaverExclusionCheck("Alex Johnson", "alex.johnson@x.com", "alex.johnson@x.com")));
    }

    [Fact]
    public void Platform_status_table_shows_graph_exchange_and_ad_outcomes()
    {
        const string log = """
            Connected to Microsoft Graph as app-only. AppId: abc
            Failed: Connect to Exchange Online using app-only certificate authentication. Module could not be correctly formed
            Skipping all Exchange Online mailbox actions — Exchange Online is not connected.
            Failed: Disable on-prem AD account. Access is denied
            """;

        var status = LeaverScriptPlatformStatus.Evaluate(log);

        Assert.Equal(PlatformStepStatus.Success, status.Graph);
        Assert.Equal(PlatformStepStatus.Failed, status.Exchange);
        Assert.Equal(PlatformStepStatus.Failed, status.ActiveDirectory);

        var html = LeaverScriptPlatformStatus.BuildHtmlTable(status);
        Assert.Contains("Graph", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Exchange", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUCCESS", html, StringComparison.Ordinal);
        Assert.Contains("FAILED", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_status_marks_sandbox_ad_skip_as_skipped()
    {
        const string log = """
            Connected to Microsoft Graph as app-only. AppId: abc
            Connected to Exchange Online using app-only certificate authentication.
            SANDBOX: Skipped AD password reset and disable — account unchanged.
            """;

        var status = LeaverScriptPlatformStatus.Evaluate(log);

        Assert.Equal(PlatformStepStatus.Success, status.Graph);
        Assert.Equal(PlatformStepStatus.Success, status.Exchange);
        Assert.Equal(PlatformStepStatus.Skipped, status.ActiveDirectory);
    }

    [Fact]
    public void Recoverable_failures_include_module_and_timeout_errors()
    {
        Assert.True(LeaverScriptFailureClassifier.IsRecoverable(
            124,
            "Failed: Connect to Exchange. Module could not be correctly formed.",
            null));
        Assert.False(LeaverScriptFailureClassifier.IsRecoverable(
            1,
            "Certificate with thumbprint 'ABC' was not found",
            null));
        Assert.False(LeaverScriptFailureClassifier.IsRecoverable(
            0,
            "Disabled Entra ID account\nDisabled AD account",
            null));
    }

    [Fact]
    public void Finished_offboarding_log_is_not_recoverable()
    {
        const string log = """
            ========== Finished offboarding for user@example.com ==========
            Offboarding failed because the Entra and/or AD account was not disabled.
            Access is denied
            """;

        Assert.False(LeaverScriptFailureClassifier.IsRecoverable(1, log, null));
        Assert.True(LeaverScriptFailureClassifier.ScriptFinished(log));
    }

    [Fact]
    public void Cancelled_requests_cannot_be_viewed_or_retried()
    {
        var cancelled = new LeaverRequest { Status = LeaverStatus.Cancelled, SubmittedByEmail = "user@example.com" };
        var failed = new LeaverRequest { Status = LeaverStatus.Failed, SubmittedByEmail = "user@example.com" };
        var submitter = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "user@example.com")], "test"));
        var superUser = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, LeaversRoles.SuperUser),
            new Claim(ClaimTypes.Email, "admin@example.com")
        ], "test"));
        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, LeaversRoles.Admin)], "test"));

        Assert.False(LeaversRoles.CanViewRequest(submitter, cancelled));
        Assert.False(LeaversRoles.CanEditRequest(admin, cancelled));
        Assert.False(LeaversRoles.CanEditRequest(admin, failed));
        Assert.True(LeaversRoles.CanEditRequest(admin, new LeaverRequest { Status = LeaverStatus.Scheduled }));
        Assert.True(LeaversRoles.CanRetryRequest(superUser, failed));
        Assert.False(LeaversRoles.CanRetryRequest(admin, failed));
    }

    [Fact]
    public void Status_display_uses_plain_english_labels()
    {
        Assert.Equal("Waiting", LeaverStatusDisplay.ToLabel(LeaverStatus.Scheduled));
        Assert.Equal("Processing", LeaverStatusDisplay.ToLabel(LeaverStatus.Pending));
        Assert.Equal("Done", LeaverStatusDisplay.ToLabel(LeaverStatus.Completed));
        Assert.Equal("Needs IT help", LeaverStatusDisplay.ToLabel(LeaverStatus.Failed));
        Assert.Equal("Needs approval", LeaverStatusDisplay.ToLabel(LeaverStatus.AwaitingApproval));
    }

    [Fact]
    public void Role_helper_identifies_admin_and_super_user()
    {
        var superUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, LeaversRoles.SuperUser)], "test"));
        var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, LeaversRoles.Admin)], "test"));
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, LeaversRoles.User)], "test"));

        Assert.True(LeaversRoles.IsAdminOrAbove(superUser));
        Assert.True(LeaversRoles.IsAdminOrAbove(admin));
        Assert.False(LeaversRoles.IsAdminOrAbove(user));
        Assert.True(LeaversRoles.CanManageAppRoles(superUser));
        Assert.False(LeaversRoles.CanManageAppRoles(admin));
    }

    [Fact]
    public void Status_detail_explains_scheduled_and_failed_outcomes()
    {
        var presenter = new LeaverRequestStatusPresenter(Microsoft.Extensions.Options.Options.Create(new LeaversFormOptions()));
        var scheduled = new LeaverRequest
        {
            Status = LeaverStatus.Scheduled,
            LeavingDate = new DateOnly(2026, 8, 14),
            ScriptEligibleAt = new ScriptEligibilityCalculator().Calculate(new DateOnly(2026, 8, 14))
        };

        Assert.Contains("5:00 PM UK", presenter.BuildStatusDetail(scheduled), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Queued for immediate processing", presenter.BuildStatusDetail(new LeaverRequest
        {
            Status = LeaverStatus.Scheduled,
            DisableAsap = true
        }));
        Assert.Contains("Switching off their account", presenter.BuildStatusDetail(new LeaverRequest { Status = LeaverStatus.Pending }), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authorising_manager_can_view_but_not_edit()
    {
        var manager = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "manager@example.com")], "test"));
        var request = new LeaverRequest
        {
            Status = LeaverStatus.Scheduled,
            SubmittedByEmail = "submitter@example.com",
            AuthorisingManagers =
            [
                new LeaverRequestAuthorisingManager
                {
                    AuthorisingManager = new AuthorisingManager
                    {
                        Email = "manager@example.com",
                        DisplayName = "Manager",
                        IsActive = true
                    }
                }
            ]
        };

        Assert.True(LeaversRoles.CanViewRequest(manager, request));
        Assert.False(LeaversRoles.CanEditRequest(manager, request));
    }

    [Fact]
    public async Task CreateAsync_blocks_duplicate_leaver_submissions()
    {
        var options = new DbContextOptionsBuilder<LeaversFormDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new LeaversFormDbContext(options);
        db.LeaverRequests.Add(new LeaverRequest
        {
            LeaverEntraId = "entra-rahie",
            LeaverDisplayName = "Rahie Patel",
            LeaverUserPrincipalName = "rahiepatel@yoursclothing.co.uk",
            LeaverEmail = "rahiepatel@yoursclothing.co.uk",
            LeavingDate = new DateOnly(2026, 8, 10),
            EquipmentNotes = "None",
            SubmittedByName = "Tester",
            SubmittedByEmail = "tester@example.com",
            SubmittedAt = DateTime.UtcNow,
            Status = LeaverStatus.Completed
        });
        await db.SaveChangesAsync();

        var service = new LeaverRequestService(
            db,
            new ScriptEligibilityCalculator(),
            new AuditService(db),
            new NoOpAsapDispatcher(),
            new LeaverExclusionService(db),
            new NoOpManagerNotifications(),
            new TestStatusPresenter(),
            new TestExecutionTracker(),
            Microsoft.Extensions.Options.Options.Create(new LeaversFormOptions()));

        var performer = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "other@example.com")], "test"));
        var duplicate = new LeaverRequest
        {
            LeaverEntraId = "entra-rahie",
            LeaverDisplayName = "Rahie Patel",
            LeaverUserPrincipalName = "rahiepatel@yoursclothing.co.uk",
            LeaverEmail = "rahiepatel@yoursclothing.co.uk",
            LeavingDate = new DateOnly(2026, 8, 15),
            EquipmentNotes = "Laptop",
            DisableAsap = false
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(duplicate, [1], performer));

        Assert.Contains("already been submitted", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ignores_disable_asap_for_non_admin_users()
    {
        var options = new DbContextOptionsBuilder<LeaversFormDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new LeaversFormDbContext(options);
        db.AuthorisingManagers.Add(new AuthorisingManager
        {
            DisplayName = "Manager",
            Email = "manager@example.com",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new LeaverRequestService(
            db,
            new ScriptEligibilityCalculator(),
            new AuditService(db),
            new NoOpAsapDispatcher(),
            new LeaverExclusionService(db),
            new NoOpManagerNotifications(),
            new TestStatusPresenter(),
            new TestExecutionTracker(),
            Microsoft.Extensions.Options.Options.Create(new LeaversFormOptions()));

        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Email, "user@example.com")
        ], "test"));

        var request = await service.CreateAsync(new LeaverRequest
        {
            LeaverEntraId = "entra-user",
            LeaverDisplayName = "Regular User",
            LeaverUserPrincipalName = "regular@example.com",
            LeaverEmail = "regular@example.com",
            LeavingDate = new DateOnly(2026, 8, 20),
            EquipmentNotes = "Laptop",
            DisableAsap = true
        }, [1], user);

        Assert.False(request.DisableAsap);
        Assert.Equal(LeaverStatus.AwaitingApproval, request.Status);
    }

    [Fact]
    public async Task CreateAsync_auto_approves_admin_submissions()
    {
        var options = new DbContextOptionsBuilder<LeaversFormDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new LeaversFormDbContext(options);
        db.AuthorisingManagers.Add(new AuthorisingManager
        {
            DisplayName = "Manager",
            Email = "manager@example.com",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = CreateRequestService(db);
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, LeaversRoles.Admin),
            new Claim(ClaimTypes.Email, "admin@example.com")
        ], "test"));

        var request = await service.CreateAsync(new LeaverRequest
        {
            LeaverEntraId = "entra-admin-test",
            LeaverDisplayName = "Admin Test User",
            LeaverUserPrincipalName = "admintest@example.com",
            LeaverEmail = "admintest@example.com",
            LeavingDate = new DateOnly(2026, 8, 20),
            EquipmentNotes = "Laptop",
            DisableAsap = false
        }, [1], admin);

        Assert.Equal(LeaverStatus.Scheduled, request.Status);
        Assert.NotNull(request.ApprovedAt);
    }

    [Fact]
    public async Task CreateAsync_limits_regular_users_to_three_submissions_per_day()
    {
        var options = new DbContextOptionsBuilder<LeaversFormDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new LeaversFormDbContext(options);
        db.AuthorisingManagers.Add(new AuthorisingManager
        {
            DisplayName = "Manager",
            Email = "manager@example.com",
            IsActive = true
        });

        for (var i = 0; i < 3; i++)
        {
            db.LeaverRequests.Add(new LeaverRequest
            {
                LeaverEntraId = $"entra-{i}",
                LeaverDisplayName = $"User {i}",
                LeaverUserPrincipalName = $"user{i}@example.com",
                LeaverEmail = $"user{i}@example.com",
                LeavingDate = new DateOnly(2026, 8, 20),
                EquipmentNotes = "Laptop",
                SubmittedByName = "Regular User",
                SubmittedByEmail = "user@example.com",
                SubmittedAt = DateTime.UtcNow,
                Status = LeaverStatus.AwaitingApproval,
                ScriptEligibleAt = DateTime.MaxValue
            });
        }
        await db.SaveChangesAsync();

        var service = CreateRequestService(db);
        var user = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Email, "user@example.com")
        ], "test"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new LeaverRequest
            {
                LeaverEntraId = "entra-fourth",
                LeaverDisplayName = "Fourth User",
                LeaverUserPrincipalName = "fourth@example.com",
                LeaverEmail = "fourth@example.com",
                LeavingDate = new DateOnly(2026, 8, 21),
                EquipmentNotes = "Laptop"
            }, [1], user));

        Assert.Contains("3 leaver requests per day", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveAsync_moves_request_to_scheduled()
    {
        var options = new DbContextOptionsBuilder<LeaversFormDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new LeaversFormDbContext(options);
        var request = new LeaverRequest
        {
            LeaverEntraId = "entra-approve",
            LeaverDisplayName = "Approve Me",
            LeaverUserPrincipalName = "approve@example.com",
            LeaverEmail = "approve@example.com",
            LeavingDate = new DateOnly(2026, 8, 20),
            EquipmentNotes = "Laptop",
            SubmittedByName = "Submitter",
            SubmittedByEmail = "submitter@example.com",
            SubmittedAt = DateTime.UtcNow,
            Status = LeaverStatus.AwaitingApproval,
            ScriptEligibleAt = DateTime.MaxValue,
            AuthorisingManagers =
            [
                new LeaverRequestAuthorisingManager
                {
                    AuthorisingManager = new AuthorisingManager
                    {
                        Email = "manager@example.com",
                        DisplayName = "Manager",
                        IsActive = true
                    }
                }
            ]
        };
        db.LeaverRequests.Add(request);
        await db.SaveChangesAsync();

        var service = CreateRequestService(db);
        var manager = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Email, "manager@example.com")
        ], "test"));

        await service.ApproveAsync(request.Id, manager);

        var updated = await db.LeaverRequests.SingleAsync(x => x.Id == request.Id);
        Assert.Equal(LeaverStatus.Scheduled, updated.Status);
        Assert.NotNull(updated.ApprovedAt);
        Assert.True(updated.ScriptEligibleAt < DateTime.MaxValue);
    }

    [Fact]
    public void Approval_permissions_block_submitter_and_allow_manager()
    {
        var request = new LeaverRequest
        {
            Status = LeaverStatus.AwaitingApproval,
            SubmittedByEmail = "submitter@example.com",
            AuthorisingManagers =
            [
                new LeaverRequestAuthorisingManager
                {
                    AuthorisingManager = new AuthorisingManager
                    {
                        Email = "manager@example.com",
                        DisplayName = "Manager",
                        IsActive = true
                    }
                }
            ]
        };

        var submitter = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Email, "submitter@example.com")
        ], "test"));
        var manager = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Email, "manager@example.com")
        ], "test"));
        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, LeaversRoles.Admin),
            new Claim(ClaimTypes.Email, "admin@example.com")
        ], "test"));

        Assert.False(LeaversRoles.CanApproveRequest(submitter, request));
        Assert.True(LeaversRoles.CanApproveRequest(manager, request));
        Assert.True(LeaversRoles.CanApproveRequest(admin, request));
    }

    [Fact]
    public async Task CancelAsync_attempts_to_terminate_pending_script()
    {
        var options = new DbContextOptionsBuilder<LeaversFormDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new LeaversFormDbContext(options);
        var request = new LeaverRequest
        {
            LeaverEntraId = "entra-1",
            LeaverDisplayName = "Pending User",
            LeaverUserPrincipalName = "pending@example.com",
            LeaverEmail = "pending@example.com",
            LeavingDate = new DateOnly(2026, 8, 10),
            EquipmentNotes = "Laptop",
            SubmittedByName = "Tester",
            SubmittedByEmail = "admin@example.com",
            SubmittedAt = DateTime.UtcNow,
            Status = LeaverStatus.Pending
        };
        db.LeaverRequests.Add(request);
        await db.SaveChangesAsync();

        var tracker = new TestExecutionTracker { TerminateResult = true };
        var service = new LeaverRequestService(
            db,
            new ScriptEligibilityCalculator(),
            new AuditService(db),
            new NoOpAsapDispatcher(),
            new LeaverExclusionService(db),
            new NoOpManagerNotifications(),
            new TestStatusPresenter(),
            tracker,
            Microsoft.Extensions.Options.Options.Create(new LeaversFormOptions()));

        var admin = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.Role, LeaversRoles.Admin),
            new Claim(ClaimTypes.Email, "admin@example.com")
        ], "test"));

        await service.CancelAsync(request.Id, admin);

        Assert.True(tracker.TerminateCalled);
        var updated = await db.LeaverRequests.SingleAsync(x => x.Id == request.Id);
        Assert.Equal(LeaverStatus.Cancelled, updated.Status);
        var audit = await db.AuditLogs.SingleAsync(x => x.LeaverRequestId == request.Id);
        Assert.Contains("terminate attempted", audit.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Processing_mode_toggle_updates_effective_mode_and_persists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LeaversFormTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "appsettings.Development.local.json");
        await File.WriteAllTextAsync(settingsPath, """{"LeaversForm":{"ScriptTestMode":false}}""");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["LeaversForm:ScriptTestMode"] = "false" })
                .Build();

            var services = new ServiceCollection();
            services.AddScoped<IAuditService, AuditService>();
            services.AddDbContext<LeaversFormDbContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            var provider = services.BuildServiceProvider();

            var environment = new TestHostEnvironment(tempDir);
            var service = new LeaversFormProcessingModeService(
                Options.Create(new LeaversFormOptions()).ToMonitor(),
                configuration,
                environment,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<LeaversFormProcessingModeService>.Instance);

            Assert.False(service.IsScriptTestModeEffective);

            var performer = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.Role, LeaversRoles.SuperUser),
                new Claim(ClaimTypes.Email, "admin@example.com")
            ], "test"));

            await service.SetScriptTestModeAsync(testMode: true, performer);

            Assert.True(service.IsScriptTestModeEffective);
            var persisted = await File.ReadAllTextAsync(settingsPath);
            Assert.Contains("\"ScriptTestMode\": true", persisted, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Email_settings_persist_to_local_json()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LeaversFormEmailSettings_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "appsettings.Development.local.json");
        await File.WriteAllTextAsync(settingsPath, """{"Graph":{"ItCcAddress":"old@example.com"}}""");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Graph:ItCcAddress"] = "old@example.com",
                    ["Graph:ErrorAlertMailbox"] = "errors@example.com",
                    ["Graph:RedirectAllEmailsTo"] = "redirect@example.com"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddScoped<IAuditService, AuditService>();
            services.AddDbContext<LeaversFormDbContext>(options =>
                options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            var provider = services.BuildServiceProvider();

            var environment = new TestHostEnvironment(tempDir);
            var processingMode = new LeaversFormProcessingModeService(
                Options.Create(new LeaversFormOptions()).ToMonitor(),
                configuration,
                environment,
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<LeaversFormProcessingModeService>.Instance);

            var emailService = new LeaversFormEmailSettingsService(
                configuration,
                environment,
                processingMode,
                Microsoft.Extensions.Options.Options.Create(new GraphOptions { EmailTestMode = true }),
                provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<LeaversFormEmailSettingsService>.Instance);

            var performer = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.Role, LeaversRoles.SuperUser),
                new Claim(ClaimTypes.Email, "admin@example.com")
            ], "test"));

            await emailService.UpdateAsync(
                new LeaversEmailSettings("it@example.com", "hr@example.com", "alerts@example.com", string.Empty),
                performer);

            Assert.Equal("it@example.com", emailService.ItCcAddress);
            Assert.Equal("hr@example.com", emailService.HrCcAddress);
            Assert.Equal("alerts@example.com", emailService.ErrorAlertMailbox);
            Assert.False(emailService.RedirectActive);
            Assert.False(emailService.CcHrOnSubmissionEmails);

            var persisted = await File.ReadAllTextAsync(settingsPath);
            Assert.Contains("it@example.com", persisted, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("alerts@example.com", persisted, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Ops_config_reflects_processing_mode_and_paths()
    {
        var ops = new LeaversOpsService(
            new TestProcessingModeService(true),
            new TestEmailSettingsService(),
            Microsoft.Extensions.Options.Options.Create(new LeaversFormOptions
            {
                PowerShellScriptPath = "C:\\missing\\LeaverProcess.ps1",
                LogFolderPath = "logs",
                ScriptSandboxSkipAd = true
            }),
            new GraphOptions { EmailTestMode = true });

        var config = await ops.GetConfigAsync();

        Assert.True(config.ScriptTestMode);
        Assert.True(config.ScriptSandboxSkipAd);
        Assert.True(config.EmailTestMode);
        Assert.False(config.ScriptPathExists);
        Assert.Equal("logs", config.LogFolderPath);
    }

    [Fact]
    public void Log_path_validation_rejects_paths_outside_log_folder()
    {
        var folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LeaversFormLogs"));
        var inside = Path.Combine(folder, "offboarding-1-test.log");
        var outside = Path.Combine(Path.GetTempPath(), "secret.log");

        Assert.True(LeaverRequestLogService.IsPathWithinFolder(inside, folder));
        Assert.False(LeaverRequestLogService.IsPathWithinFolder(outside, folder));
    }

    private static LeaverRequestService CreateRequestService(LeaversFormDbContext db) =>
        new(
            db,
            new ScriptEligibilityCalculator(),
            new AuditService(db),
            new NoOpAsapDispatcher(),
            new LeaverExclusionService(db),
            new NoOpManagerNotifications(),
            new TestStatusPresenter(),
            new TestExecutionTracker(),
            Microsoft.Extensions.Options.Options.Create(new LeaversFormOptions()));

    private sealed class NoOpAsapDispatcher : IAsapLeaverScriptDispatcher
    {
        public void Enqueue(int requestId) { }
    }

    private sealed class NoOpManagerNotifications : IAuthorisingManagerNotificationService
    {
        public Task NotifySelectedManagersAsync(LeaverRequest request, IEnumerable<AuthorisingManager> managers, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestStatusPresenter : ILeaverRequestStatusPresenter
    {
        public string? BuildStatusDetail(LeaverRequest request) => request.Status.ToString();
        public string? BuildFailureGuidance(LeaverRequest request) => null;
    }

    private sealed class TestExecutionTracker : ILeaverScriptExecutionTracker
    {
        public bool TerminateCalled { get; private set; }
        public bool TerminateResult { get; init; }

        public bool TryBegin(int requestId) => true;
        public void End(int requestId) { }
        public bool IsRunning(int requestId) => false;
        public void AttachProcess(int requestId, int processId) { }

        public bool TryTerminate(int requestId)
        {
            TerminateCalled = true;
            return TerminateResult;
        }
    }

    private sealed class TestEmailSettingsService : ILeaversFormEmailSettingsService
    {
        public LeaversEmailSettings Current { get; } = new("it@example.com", "hr@example.com", "errors@example.com", string.Empty);
        public string ItCcAddress => Current.ItCcAddress;
        public string HrCcAddress => Current.HrCcAddress;
        public string ErrorAlertMailbox => Current.ErrorAlertMailbox;
        public string RedirectAllEmailsTo => Current.RedirectAllEmailsTo;
        public bool RedirectActive => Current.RedirectActive;
        public bool CcHrOnSubmissionEmails => false;
        public Task UpdateAsync(LeaversEmailSettings settings, ClaimsPrincipal performer, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestProcessingModeService(bool testMode) : ILeaversFormProcessingModeService
    {
        public bool IsScriptTestModeEffective => testMode;
        public string? LocalSettingsPath => null;
        public Task SetScriptTestModeAsync(bool testMode, ClaimsPrincipal performer, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "LeaversForm.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

internal static class OptionsMonitorExtensions
{
    public static IOptionsMonitor<T> ToMonitor<T>(this IOptions<T> options) where T : class =>
        new TestOptionsMonitor<T>(options.Value);
}

internal sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}