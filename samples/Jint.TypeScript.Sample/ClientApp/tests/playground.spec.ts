import { test, expect, type Page } from '@playwright/test';

test('initializes embedded editors without browser errors', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    await open(page);
    const icon = page.locator('link[rel="icon"]');
    await expect(icon).toHaveAttribute('type', 'image/svg+xml');
    expect(await icon.evaluate(async element => {
        const response = await fetch((element as HTMLLinkElement).href);
        return response.ok && (await response.text()).includes('<svg');
    })).toBe(true);
    await page.getByRole('textbox', { name: 'TypeScript code', exact: true }).focus();
    await page.keyboard.press('ControlOrMeta+Home');
    for (let index = 0; index < 11; index++) await page.keyboard.press('ArrowRight');
    await page.keyboard.press('Shift+F12');
    await expect(page.locator('.peekview-widget')).toBeVisible();
    await expect(page.locator('.peekview-widget .view-lines')).toContainText('greet');
    // Embedded contributions initialize on idle after the references pane appears.
    await page.waitForTimeout(1000);
    expect(errors).toEqual([]);
});

async function open(page: Page, example = 'imports') {
    await page.goto('/');
    await expect(page.locator('#language-status')).toHaveText(/^TypeScript \d+\./);
    await page.getByLabel('Example', { exact: true }).selectOption(example);
    await expect(page.locator('#language-status')).toHaveText(/^TypeScript \d+\./);
    await expect(page.getByRole('tab', { name: example === 'migration' ? 'migration/main.js' : 'main.ts', exact: true })).toBeVisible();
}
async function edit(page: Page, text: string) {
    const input = page.getByRole('textbox', { name: /^(TypeScript|JavaScript) code$/ });
    await input.focus();
    await page.keyboard.press('ControlOrMeta+A');
    // Paste a whole source file; typing a multiline string triggers editor auto-indent/bracket insertion.
    await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
    await page.evaluate(value => navigator.clipboard.writeText(value), text);
    await page.keyboard.press('ControlOrMeta+V');
}
test('executes every real example and displays host logs', async ({ page }) => {
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('/');
    await expect(page.locator('#language-status')).toHaveText(/^TypeScript \d+\./);
    for (const [id, output] of [['pricing', '10625'], ['validation', 'customer.email'], ['webhook', 'evt-2048'], ['migration', '5850'], ['imports', 'Hello, world!']]) {
        await page.getByLabel('Example', { exact: true }).selectOption(id);
        await expect(page.locator('#language-status')).toHaveText(/^TypeScript \d+\./);
        await expect(page.locator('#problem-count')).toHaveText('0');
        await page.getByRole('button', { name: /Run/ }).click();
        await expect(page.locator('#run-status')).toContainText('Completed');
        await expect(page.locator('#result')).toContainText(output);
        await expect(page.locator('#logs')).not.toBeEmpty();
    }
    expect(errors).toEqual([]);
    await page.screenshot({ path: '../../../artifacts/dx-review/playground.png', fullPage: true });
});
test('adds an imported file, executes it, and restores edits after reload', async ({ page }) => {
    await open(page);
    await page.getByRole('button', { name: 'Add file', exact: true }).click();
    await page.getByLabel('Filename', { exact: true }).fill('lib/helper.ts');
    await page.locator('#save-file').click();
    await edit(page, 'export function double(value: number): number { return value * 2; }');
    await page.getByRole('tab', { name: 'main.ts', exact: true }).click();
    await edit(page, "import { double } from './lib/helper.ts';\nexport function run() { return double(21); }");
    await page.getByRole('button', { name: /Run/ }).click();
    await expect(page.locator('#result')).toHaveText('42');
    await expect(page.locator('#save-state')).toHaveText('Saved locally');
    await page.reload();
    await expect(page.getByRole('tab', { name: 'lib/helper.ts', exact: true })).toBeVisible();
    await page.getByRole('button', { name: /Run/ }).click();
    await expect(page.locator('#result')).toHaveText('42');
});
test('offers imported function completions and diagnoses types across files', async ({ page }) => {
    await open(page);
    await edit(page, "import { greet } from './greet.ts';\nexport function run() { return gre");
    await page.keyboard.press('Control+Space');
    await expect(page.locator('.suggest-widget.visible')).toContainText('greet');
    await page.keyboard.press('Escape');
    await edit(page, "import { greet } from './greet.ts';\nexport function run() { return greet(42); }");
    await page.getByRole('tab', { name: /Problems/ }).click();
    await expect(page.locator('#problems')).toContainText("not assignable to parameter of type 'string'");
    await page.getByRole('tab', { name: 'greet.ts', exact: true }).click();
    await edit(page, 'export function greet(name: number): string { return String(name); }');
    await expect(page.locator('#problem-count')).toHaveText('0');
});
test('supports host IntelliSense and rejects runtime TypeScript transforms in the editor', async ({ page }) => {
    await open(page);
    await edit(page, 'export function run() { host.');
    await page.keyboard.press('Control+Space');
    await expect(page.locator('.suggest-widget.visible')).toContainText('Log');
    await page.keyboard.press('Escape');
    await edit(page, 'enum Status { Ready, Done }\nexport function run() { return Status.Ready; }');
    await page.getByRole('tab', { name: /Problems/ }).click();
    await expect(page.locator('#problems')).toContainText('erasableSyntaxOnly');
});
test('navigates to definitions in another editor file', async ({ page }) => {
    await open(page);
    await edit(page, "import { greet } from './greet.ts';\nexport function run() { return greet('world'); }");
    const input = page.getByRole('textbox', { name: 'TypeScript code' });
    await input.focus();
    await page.keyboard.press('ControlOrMeta+Home');
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Home');
    for (let index = 0; index < 32; index++) await page.keyboard.press('ArrowRight');
    await page.keyboard.press('F12');
    await expect(page.getByRole('tab', { name: 'greet.ts', exact: true })).toHaveAttribute('aria-selected', 'true');
});
test('renames an exported symbol and updates imports in other files', async ({ page }) => {
    await open(page);
    await page.getByRole('tab', { name: 'greet.ts', exact: true }).click();
    await page.getByRole('textbox', { name: 'TypeScript code' }).focus();
    await page.keyboard.press('ControlOrMeta+Home');
    for (let index = 0; index < 18; index++) await page.keyboard.press('ArrowRight');
    await page.keyboard.press('F2');
    const rename = page.getByRole('textbox', { name: /Rename input/ });
    await rename.fill('welcome');
    await rename.press('Enter');
    await page.getByRole('tab', { name: 'main.ts', exact: true }).click();
    await expect(page.locator('.view-lines')).toContainText('welcome');
    await page.getByRole('button', { name: /Run/ }).click();
    await expect(page.locator('#result')).toContainText('Hello, world!');
});
test('shows original runtime locations and navigates to the failing dependency', async ({ page }) => {
    await open(page);
    await page.getByRole('tab', { name: 'greet.ts', exact: true }).click();
    await edit(page, "export function greet(name: string): string {\n    throw new Error('Deliberate failure');\n}");
    await page.getByRole('button', { name: /Run/ }).click();
    await expect(page.locator('#runtime-error')).toContainText('Deliberate failure');
    await expect(page.locator('#runtime-error')).toContainText('greet.ts:2:');
    await page.getByRole('tab', { name: 'main.ts', exact: true }).click();
    await page.locator('#runtime-error button').click();
    await expect(page.getByRole('tab', { name: 'greet.ts', exact: true })).toHaveAttribute('aria-selected', 'true');
});
test('explains unsupported enums in imported files and runs the suggested alternative', async ({ page }) => {
    await open(page);
    await page.getByRole('tab', { name: 'greet.ts', exact: true }).click();
    await edit(page, "export enum Greeting { Hello = 'Hello' }\nexport function greet(name: string): string { return `${Greeting.Hello}, ${name}!`; }");
    await page.getByRole('tab', { name: 'main.ts', exact: true }).click();
    await page.getByRole('button', { name: /Run/ }).click();
    await expect(page.locator('#runtime-error')).toContainText('UnsupportedEnum');
    await expect(page.locator('#runtime-error')).toContainText("'as const' and a union type");
    await expect(page.locator('#runtime-error')).toContainText('greet.ts:1:8');
    await page.locator('#runtime-error button').click();
    await expect(page.getByRole('tab', { name: 'greet.ts', exact: true })).toHaveAttribute('aria-selected', 'true');
    await edit(page, "const Greeting = { Hello: 'Hello' } as const;\ntype Greeting = typeof Greeting[keyof typeof Greeting];\nexport function greet(name: string): string {\n    const greeting: Greeting = Greeting.Hello;\n    return `${greeting}, ${name}!`;\n}");
    await expect(page.locator('#problem-count')).toHaveText('0');
    await page.getByRole('button', { name: /Run/ }).click();
    await expect(page.locator('#run-status')).toContainText('Completed');
    await expect(page.locator('#result')).toContainText('Hello, world!');
});
test('validates input JSON and stops a pending execution', async ({ page }) => {
    await open(page);
    await page.getByRole('tab', { name: 'Input', exact: true }).click();
    await page.getByLabel('JSON input', { exact: true }).fill('{invalid');
    await page.getByRole('button', { name: /Run/ }).click();
    await expect(page.locator('#toast')).toContainText('valid JSON');
    await page.getByLabel('JSON input', { exact: true }).fill('{}');
    await edit(page, 'export async function run() { return await new Promise(() => {}); }');
    await page.getByRole('button', { name: /Run/ }).click();
    await page.getByRole('button', { name: /Stop/ }).click();
    await expect(page.locator('#run-status')).toHaveText('Canceled');
    await expect(page.getByRole('button', { name: /Run/ })).toBeEnabled();
});

test('uses real JSDoc IntelliSense and preserves execution through conversion, Undo and reload', async ({ page }) => {
    await open(page, 'migration');
    await expect(page.locator('#file-language')).toHaveText('JavaScript · JSDoc');
    const examples = await (await page.request.get('/api/examples')).json();
    const original = examples.find((example: { id: string }) => example.id === 'migration').files as Record<string,string>;
    const completionSource = "/** @param {import('./quote.js').QuoteInput} input */\nexport function run(input) { return input";
    await edit(page, completionSource);
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('jint-typescript-playground-v1')!).files['migration/main.js'])).toBe(completionSource);
    await page.keyboard.press('ControlOrMeta+End');
    await page.keyboard.type('.');
    await page.keyboard.press('Control+Space');
    await expect(page.locator('.suggest-widget.visible')).toContainText('discountPercent');
    await expect(page.locator('.suggest-widget.visible')).toContainText('lines');
    await page.screenshot({ path: '../../../artifacts/jsdoc-conversion/playground-jsdoc.png', fullPage: true });
    await page.keyboard.press('Escape');
    await edit(page, original['migration/main.js']!);
    await expect(page.locator('#problem-count')).toHaveText('0');
    await page.getByRole('button', { name: /Run/ }).click();
    await expect(page.locator('#result')).toContainText('5850');
    const expected = await page.locator('#result').innerText();
    for (const name of ['subtotal', 'quote', 'main']) {
        await page.getByRole('tab', { name: `migration/${name}.js`, exact: true }).click();
        await page.getByRole('button', { name: 'Convert to TypeScript', exact: true }).click();
        await expect(page.getByRole('tab', { name: `migration/${name}.ts`, exact: true })).toHaveAttribute('aria-selected','true');
        await expect(page.locator('#file-language')).toHaveText('TypeScript');
        await expect(page.locator('#problem-count')).toHaveText('0');
        await expect(page.locator('#conversion-status')).toBeHidden();
        await page.getByRole('button', { name: /Run/ }).click();
        await expect(page.locator('#run-status')).toHaveText(/Completed/);
        await expect(page.locator('#result')).toHaveText(expected);
        if (name === 'subtotal') {
            await page.getByRole('button', { name: 'Undo conversion', exact: true }).click();
            await expect(page.getByRole('tab', { name: 'migration/subtotal.js', exact: true })).toHaveAttribute('aria-selected','true');
            await expect(page.locator('#file-language')).toHaveText('JavaScript · JSDoc');
            await expect(page.locator('#problem-count')).toHaveText('0');
            await page.getByRole('button', { name: /Run/ }).click();
            await expect(page.locator('#run-status')).toHaveText(/Completed/);
            await expect(page.locator('#result')).toHaveText(expected);
            await page.getByRole('button', { name: 'Convert to TypeScript', exact: true }).click();
            await expect(page.getByRole('tab', { name: 'migration/subtotal.ts', exact: true })).toBeVisible();
        }
    }
    await page.reload();
    await expect(page.locator('#language-status')).toHaveText(/^TypeScript \d+\./);
    await expect(page.getByRole('tab', { name: 'migration/main.ts', exact: true })).toBeVisible();
    await expect(page.locator('#problem-count')).toHaveText('0');
    await page.getByRole('button', { name: /Run/ }).click();
    await expect(page.locator('#result')).toHaveText(expected);
});

test('preserves source and JavaScript mode when conversion is unsupported', async ({ page }) => {
    await open(page, 'migration');
    const source = '/** @callback Mapper\n * @param {string} value\n * @returns {number}\n */\nexport function run() { return 42; }';
    await edit(page, source);
    await page.getByRole('button', { name: 'Convert to TypeScript', exact: true }).click();
    await expect(page.locator('#conversion-status')).toContainText('@callback is not supported');
    await expect(page.getByRole('tab', { name: 'migration/main.js', exact: true })).toHaveAttribute('aria-selected','true');
    await expect(page.locator('#file-language')).toHaveText('JavaScript · JSDoc');
    await expect(page.getByRole('tab', { name: 'migration/main.ts', exact: true })).toHaveCount(0);
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('jint-typescript-playground-v1')!).files['migration/main.js'])).toBe(source);
    await page.getByRole('button', { name: /Run/ }).click();
    await expect(page.locator('#result')).toHaveText('42');
});

test('conversion refuses a filename collision and preserves subsequent edits', async ({ page }) => {
    await open(page, 'migration');
    await page.getByRole('button', { name: 'Add file', exact: true }).click();
    await page.getByLabel('Filename', { exact: true }).fill('migration/main.ts');
    await page.locator('#save-file').click();
    await page.getByRole('tab', { name: 'migration/main.js', exact: true }).click();
    await page.getByRole('button', { name: 'Convert to TypeScript', exact: true }).click();
    await expect(page.locator('#conversion-status')).toContainText('already exists');
    await expect(page.locator('#file-language')).toHaveText('JavaScript · JSDoc');
    await page.getByRole('tab', { name: 'migration/subtotal.js', exact: true }).click();
    await page.getByRole('button', { name: 'Convert to TypeScript', exact: true }).click();
    await expect(page.getByRole('tab', { name: 'migration/subtotal.ts', exact: true })).toBeVisible();
    await edit(page, 'export function calculateSubtotal() { return 100; }');
    await expect(page.getByRole('button', { name: 'Undo conversion', exact: true })).toBeHidden();
    await expect(page.locator('.view-lines')).toContainText('return 100');
});

test('loads conversion on demand and preserves edits made while its worker starts', async ({ page }) => {
    let requested = false;
    let release!: () => void;
    const blocked = new Promise<void>(resolve => { release = resolve; });
    await page.route('**/conversion.worker-*.js', async route => { requested = true; await blocked; await route.continue(); });
    try {
        await open(page, 'migration');
        expect(requested).toBe(false);
        await page.getByRole('button', { name: 'Convert to TypeScript', exact: true }).click();
        await expect.poll(() => requested).toBe(true);
        const newer = 'export function run() { return 123; }';
        await edit(page, newer);
        await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem('jint-typescript-playground-v1')!).files['migration/main.js'])).toBe(newer);
        release();
        await expect(page.locator('#toast')).toContainText('workspace changed during conversion');
        await expect(page.getByRole('tab', { name: 'migration/main.js', exact: true })).toBeVisible();
        await expect(page.getByRole('tab', { name: 'migration/main.ts', exact: true })).toHaveCount(0);
        await page.getByRole('button', { name: /Run/ }).click();
        await expect(page.locator('#result')).toHaveText('123');
    } finally { release(); }
});

test('checks JavaScript callers of a converted helper and supports keyboard Undo', async ({ page }) => {
    await open(page, 'migration');
    await page.getByRole('tab', { name: 'migration/subtotal.js', exact: true }).click();
    await page.getByRole('button', { name: 'Convert to TypeScript', exact: true }).click();
    await expect(page.getByRole('tab', { name: 'migration/subtotal.ts', exact: true })).toHaveAttribute('aria-selected','true');
    await page.getByRole('textbox', { name: 'TypeScript code', exact: true }).focus();
    await page.keyboard.press('ControlOrMeta+Z');
    await expect(page.getByRole('tab', { name: 'migration/subtotal.js', exact: true })).toHaveAttribute('aria-selected','true');
    await page.getByRole('button', { name: 'Convert to TypeScript', exact: true }).click();
    await expect(page.getByRole('tab', { name: 'migration/subtotal.ts', exact: true })).toBeVisible();
    await page.getByRole('tab', { name: 'migration/main.js', exact: true }).click();
    await edit(page, "import { calculateSubtotal } from './subtotal.ts';\nexport function run() { return calculateSubtotal('invalid'); }");
    await page.getByRole('tab', { name: /Problems/ }).click();
    await expect(page.locator('#problems')).toContainText("not assignable to parameter of type 'CartLine[]'");
});
