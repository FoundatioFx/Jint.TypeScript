import { defineConfig } from '@playwright/test';

export default defineConfig({
    testDir: './tests', fullyParallel: false, timeout: 30_000,
    use: { baseURL: 'http://localhost:5178', viewport: { width: 1440, height: 950 }, trace: 'retain-on-failure' },
    webServer: {
        command: 'dotnet run --project .. -c Release -f net10.0 --no-build --no-restore',
        url: 'http://localhost:5178/api/health', reuseExistingServer: !process.env.CI, timeout: 60_000
    }
});
