/// <reference types="vite/client" />

interface ImportMetaEnv {
	readonly VITE_URL_ACCESS: string;
	readonly VITE_GATEWAY_URL: string;
}

interface ImportMeta {
	readonly env: ImportMetaEnv;
}
