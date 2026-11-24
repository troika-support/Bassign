using Bassign.Domain.Tasks;
using Bassign.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bassign.Infrastructure.Persistence.Seed;

/// <summary>Dev-only data: the three roles, an Admin user, and a couple of sample tasks.</summary>
public static class DevSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var clock = services.GetRequiredService<TimeProvider>();

        foreach (var role in new[] { "Admin", "Manager", "Staff" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        const string adminEmail = "admin@bassign.local";
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                DisplayName = "Admin",
                IsActive = true
            };
            await userManager.CreateAsync(admin, "Admin!2345");   // dev-only password
            await userManager.AddToRoleAsync(admin, "Admin");
        }

        // Dev-only teammates so Team, Assign and Workload have people to show.
        foreach (var (email, name, role, password) in new[]
        {
            ("manager@bassign.local", "Chipo Moyo", "Manager", "Manager!2345"),
            ("tendai@bassign.local", "Tendai Ncube", "Staff", "Staff!2345"),
            ("rudo@bassign.local", "Rudo Chikwanha", "Staff", "Staff!2345")
        })
        {
            if (await userManager.FindByEmailAsync(email) is not null) continue;
            var person = new ApplicationUser
            {
                UserName = email, Email = email, EmailConfirmed = true, DisplayName = name, IsActive = true
            };
            await userManager.CreateAsync(person, password);   // dev-only passwords
            await userManager.AddToRoleAsync(person, role);
        }

        if (!await db.Tasks.AnyAsync(cancellationToken))
        {
            var now = clock.GetUtcNow();
            db.Tasks.Add(TaskItem.Create("Stock count — north warehouse", admin.Id, now,
                detail: "Count all pallets and reconcile.", dueDate: now.AddDays(2)));
            db.Tasks.Add(TaskItem.Create("Deliver order #4821 to Msasa", admin.Id, now,
                detail: "3 pallets. Call the site contact on arrival.", dueDate: now.AddHours(6)));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
