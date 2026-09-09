import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

const __dirname = import.meta.dirname;

const PROBE_TIMEOUT_MS = 4000;

const resolveGatewayTarget = async (candidates: string[]): Promise<string> => {
	if (candidates.length === 1) return candidates[0];

	const attempts = candidates.map(async (url) => {
		await fetch(`${url}/auth/me`, { signal: AbortSignal.timeout(PROBE_TIMEOUT_MS) });
		return url;
	});

	return Promise.any(attempts).catch(() => candidates[0]);
};

export default defineConfig(async ({ mode }) => {
	const env = loadEnv(mode, __dirname, "VITE_");

	if (!env.VITE_GATEWAY_URL) {
		throw new Error("VITE_GATEWAY_URL is required in frontEnd/.env");
	}

	if (!env.VITE_URL_ACCESS) {
		throw new Error("VITE_URL_ACCESS is required in frontEnd/.env");
	}

	const candidates = env.VITE_GATEWAY_URL.split(",")
		.map((url) => url.trim())
		.filter(Boolean);

	const gatewayTarget = await resolveGatewayTarget(candidates);
	const gatewayProxy = { target: gatewayTarget, changeOrigin: true, secure: false };

	let urlAccess = env.VITE_URL_ACCESS;
	while (urlAccess.endsWith("/")) {
		urlAccess = urlAccess.slice(0, -1);
	}

	return {
		base: "./",
		// El Gateway sirve la SPA, asi que el navegador siempre habla con su
		// propio origen: la variable solo alimenta el proxy de dev, y llevarla
		// al bundle dispararia un preflight CORS contra otro host.
		define: {
			"import.meta.env.VITE_GATEWAY_URL": JSON.stringify(""),
		},
		plugins: [react()],
		server: {
			port: 5180,
			proxy: {
				[urlAccess]: gatewayProxy,
				"/auth": gatewayProxy,
			},
		},
		build: {
			outDir: "dist",
			emptyOutDir: true,
			sourcemap: false,
		},
	};
});
