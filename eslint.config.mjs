import eslint from '@eslint/js';
import tseslint from 'typescript-eslint';
import { includeIgnoreFile } from "@eslint/compat";
import { fileURLToPath } from "node:url";

const gitignorePath = fileURLToPath(new URL(".gitignore", import.meta.url));

export default tseslint.config(
	eslint.configs.recommended,
	tseslint.configs.recommended,
	includeIgnoreFile(gitignorePath, "Imported .gitignore patterns")
);