// Validates load result files against result.schema.json (which extends the shared ADR 0003 schema).
// Usage: node tools/validate-results.mjs <file-or-directory>...
// Directories are searched recursively for *.json; results/raw/ is skipped because it holds raw k6 output.

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';

const loadDir = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repoRoot = resolve(loadDir, '..', '..', '..');
const readJson = (path) => JSON.parse(readFileSync(path, 'utf8'));

// strictRequired would reject result.schema.json: it requires properties that are defined in the referenced base schema.
const ajv = new Ajv2020({ allErrors: true, strict: true, strictRequired: false });
addFormats(ajv);
ajv.addSchema(readJson(join(repoRoot, 'docs', 'schemas', 'load-result.schema.json')));
const validate = ajv.compile(readJson(join(loadDir, 'result.schema.json')));

function collect(path) {
    if (!statSync(path).isDirectory()) {
        return [path];
    }
    return readdirSync(path)
        .filter((name) => name !== 'raw')
        .flatMap((name) => {
            const child = join(path, name);
            return statSync(child).isDirectory() ? collect(child) : name.endsWith('.json') ? [child] : [];
        });
}

const files = process.argv.slice(2).flatMap((arg) => collect(resolve(arg)));
if (files.length === 0) {
    console.error('No result files found.');
    process.exit(1);
}

let invalid = 0;
for (const file of files) {
    const name = relative(process.cwd(), file);
    let document;
    try {
        document = readJson(file);
    } catch (error) {
        invalid++;
        console.error(`INVALID ${name}: ${error.message}`);
        continue;
    }

    if (validate(document)) {
        console.log(`valid   ${name}`);
    } else {
        invalid++;
        console.error(`INVALID ${name}`);
        for (const error of validate.errors) {
            console.error(`  ${error.instancePath || '/'} ${error.message}`);
        }
    }
}

console.log(`${files.length - invalid} of ${files.length} result files are valid.`);
process.exit(invalid === 0 ? 0 : 1);
