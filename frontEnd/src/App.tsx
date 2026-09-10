import { Box, CircularProgress, CssBaseline, ThemeProvider, type PaletteMode } from "@mui/material";
import { useCallback, useEffect, useMemo, useState } from "react";
import { buildTheme } from "./theme";
import { SessionContext, type SessionUser } from "./auth/SessionContext";
import { authApi } from "./api/endpoints";
import { onUnauthorized } from "./api/client";
import { getPublicKeyBase64, resetKeyPair } from "./auth/dpop";
import LoginPage from "./pages/LoginPage";
import AppLayout from "./layout/AppLayout";

const THEME_KEY = "diarspeicher-theme-mode";

export default function App() {
	const [mode, setMode] = useState<PaletteMode>(() => {
		try {
			return (localStorage.getItem(THEME_KEY) as PaletteMode) || "dark";
		} catch {
			return "dark";
		}
	});

	const [user, setUser] = useState<SessionUser | null>(null);
	const [loading, setLoading] = useState(true);

	const toggleTheme = useCallback(() => {
		setMode((prev) => {
			const next = prev === "dark" ? "light" : "dark";
			try {
				localStorage.setItem(THEME_KEY, next);
			} catch {
				/* storage error */
			}
			return next;
		});
	}, []);

	const refreshUser = useCallback(async () => {
		try {
			const res = await authApi.me();
			if (res?.user) {
				setUser(res.user);
			} else {
				setUser(null);
			}
		} catch {
			setUser(null);
		}
	}, []);

	const login = useCallback(
		async (username: string, password: string) => {
			let clientPubkey: string | undefined;
			try {
				clientPubkey = await getPublicKeyBase64();
			} catch (e) {
				console.warn("Could not generate client public key:", e);
			}

			const res = await authApi.login({
				username,
				password,
				client_pubkey: clientPubkey,
			});

			if (res?.requires_first_admin) {
				return res;
			}

			if (res?.user) {
				setUser(res.user);
			} else {
				await refreshUser();
			}
			return res;
		},
		[refreshUser]
	);

	const registerFirstAdmin = useCallback(
		async (username: string, password: string) => {
			let clientPubkey: string | undefined;
			try {
				clientPubkey = await getPublicKeyBase64();
			} catch (e) {
				console.warn("Could not generate client public key:", e);
			}

			const res = await authApi.registerFirstAdmin({
				username,
				password,
				client_pubkey: clientPubkey,
			});

			if (res?.user) {
				setUser(res.user);
			} else {
				await refreshUser();
			}
		},
		[refreshUser]
	);

	const logout = useCallback(async () => {
		try {
			await authApi.logout();
		} catch {
			/* ignore logout api error */
		} finally {
			try {
				await resetKeyPair();
			} catch {
				/* ignore key reset error */
			}
			setUser(null);
		}
	}, []);

	useEffect(() => {
		onUnauthorized(() => {
			setUser(null);
		});

		const initSession = async () => {
			await refreshUser();
			setLoading(false);
		};

		void initSession();
	}, [refreshUser]);

	const theme = useMemo(() => buildTheme(mode), [mode]);

	const sessionValue = useMemo(
		() => ({
			user,
			loading,
			login,
			registerFirstAdmin,
			logout,
			refreshUser,
		}),
		[user, loading, login, registerFirstAdmin, logout, refreshUser]
	);

	let mainContent: React.ReactNode;
	if (loading) {
		mainContent = (
			<Box
				sx={{
					minHeight: "100vh",
					display: "flex",
					alignItems: "center",
					justifyContent: "center",
					backgroundColor: "background.default",
				}}
			>
				<CircularProgress />
			</Box>
		);
	} else if (!user) {
		mainContent = <LoginPage />;
	} else {
		mainContent = <AppLayout onToggleTheme={toggleTheme} />;
	}

	return (
		<ThemeProvider theme={theme}>
			<CssBaseline />
			<SessionContext.Provider value={sessionValue}>
				{mainContent}
			</SessionContext.Provider>
		</ThemeProvider>
	);
}
