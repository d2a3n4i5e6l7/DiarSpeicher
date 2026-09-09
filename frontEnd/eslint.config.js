import js from "@eslint/js";
import { defineConfig, globalIgnores } from "eslint/config";
import globals from "globals";
import tseslint from "typescript-eslint";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import prettier from "eslint-config-prettier/flat";

export default defineConfig([
	globalIgnores(["**/dist", "**/node_modules"]),
	{
		files: ["src/**/*.{ts,tsx}"],
		extends: [js.configs.recommended, tseslint.configs.recommendedTypeChecked, reactHooks.configs.flat["recommended-latest"], reactRefresh.configs.vite, prettier],
		languageOptions: {
			ecmaVersion: 2022,
			globals: globals.browser,
			parserOptions: {
				projectService: true,
				tsconfigRootDir: import.meta.dirname,
			},
		},
	},
	{
		files: ["vite.config.ts"],
		extends: [js.configs.recommended, tseslint.configs.recommended, prettier],
		languageOptions: { globals: globals.node },
	},
]);
