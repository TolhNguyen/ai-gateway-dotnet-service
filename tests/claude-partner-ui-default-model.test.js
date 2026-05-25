const assert = require('assert');
const fs = require('fs');
const path = require('path');

const repoRoot = path.resolve(__dirname, '..');
const appJs = fs.readFileSync(path.join(repoRoot, 'src/AiGateway.Api/wwwroot/app/app.js'), 'utf8');
const openApi = fs.readFileSync(path.join(repoRoot, 'contracts/openapi.yaml'), 'utf8');
const migrationsSql = fs
  .readdirSync(path.join(repoRoot, 'migrations'))
  .filter((name) => name.endsWith('.sql'))
  .sort()
  .map((name) => fs.readFileSync(path.join(repoRoot, 'migrations', name), 'utf8'))
  .join('\n');

assert(
  /code:\s*['"]claude['"]/.test(appJs),
  'non-admin partner fallback list should include Claude'
);

assert(
  /name=["']defaultModelCode["']/.test(appJs),
  'account key form should expose a defaultModelCode field'
);

assert(
  /<select name="defaultModelCode"/.test(appJs),
  'default model field should be a select so labels cannot be submitted as model codes'
);

assert(
  /value="\$\{escape\(m\.code\)\}"/.test(appJs),
  'default model options should submit the internal model code'
);

assert(
  /Gemini 2\.5 Flash \(text-fast\)/.test(appJs),
  'Gemini 2.5 Flash should be an allowed default model label'
);

assert(
  /Gemini 2\.0 Flash \(text-fast\)/.test(appJs),
  'Gemini 2.0 Flash should be an allowed default model label'
);

assert(
  /Gemini 2\.5 Pro \(text-pro\)/.test(appJs),
  'Gemini 2.5 Pro should be an allowed default model label'
);

assert(
  /defaultModelCode:\s*f\.get\(['"]defaultModelCode['"]\)/.test(appJs),
  'create key request should send defaultModelCode'
);

assert(
  /(?:updateDefaultModel:\s*true|body\.updateDefaultModel\s*=\s*true)/.test(appJs),
  'edit key request should opt in when updating defaultModelCode'
);

assert(
  /\('claude'\s*,\s*'Anthropic Claude'/.test(migrationsSql),
  'seed data should include the Claude partner'
);

assert(
  /p\.code='claude'/.test(migrationsSql),
  'seed data should include Claude model routes'
);

assert(
  /name=["']useCaveman["']/.test(appJs),
  'token creation form should expose the useCaveman checkbox'
);

assert(
  /useCaveman:\s*f\.get\(['"]useCaveman['"]\)\s*===\s*['"]on['"]/.test(appJs),
  'create token request should send useCaveman'
);

assert(
  /Response Style/.test(appJs),
  'token table should show response style'
);

assert(
  /response_style\s+VARCHAR\(30\)\s+NOT NULL\s+DEFAULT ['"]normal['"]/.test(migrationsSql),
  'migration should add PAT response_style with normal default'
);

assert(
  /useCaveman:\s*\n\s*type:\s*boolean/.test(openApi),
  'OpenAPI should include useCaveman'
);

assert(
  /responseStyle:\s*\{\s*type:\s*string,\s*enum:\s*\[normal,\s*caveman\]\s*\}/.test(openApi),
  'OpenAPI should include responseStyle enum'
);
