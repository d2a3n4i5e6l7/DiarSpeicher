import { Box, CircularProgress, CssBaseline, ThemeProvider, type PaletteMode } from "@mui/material";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Navigate, Route, Routes } from "react-router-dom";
import { buildTheme } from "./theme";
import { ColorModeContext } from "./theme/ColorModeContext";
import { SessionContext, type SessionUser } from "./auth/SessionContext";
import { authApi } from "./api/endpoints";
import { onUnauthorized } from "./api/client";
import { getPublicKeyBase64, resetKeyPair } from "./auth/dpop";
import LoginPage from "./pages/LoginPage";
import AppLayout from "./layout/AppLayout";
import UsersPage from "./pages/UsersPage";
import RolesPage from "./pages/RolesPage";
import UploadPage from "./pages/UploadPage";
import LibrariesPage from "./pages/LibrariesPage";
import HomePage from "./pages/HomePage";
import SeriesGridPage from "./pages/SeriesGridPage";
import SeriesDetailPage from "./pages/SeriesDetailPage";
import MediaDetailPage from "./pages/MediaDetailPage";
import LibraryDetailPage from "./pages/LibraryDetailPage";
import MetadataPage from "./pages/MetadataPage";
import ReaderPage from "./pages/ReaderPage";
import { LibraryFilterContext, LIBRARY_FILTER_KEY } from "./catalog/LibraryFilterContext";

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

	const [libraryId, setLibraryIdState] = useState<string | null>(() => {
		try {
			return localStorage.getItem(LIBRARY_FILTER_KEY);
		} catch {
			return null;
		}
	});

	const setLibraryId = useCallback((next: string | null) => {
		setLibraryIdState(next);
		try {
			if (next === null) {
				localStorage.removeItem(LIBRARY_FILTER_KEY);
			} else {
				localStorage.setItem(LIBRARY_FILTER_KEY, next);
			}
		} catch {
			/* sin almacenamiento el filtro dura lo que la sesión */
		}
	}, []);

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

	const theme = useMemo(() => buildTheme(), []);
	const colorMode = useMemo(() => ({ mode, toggle: toggleTheme }), [mode, toggleTheme]);

	const libraryFilter = useMemo(() => ({ libraryId, setLibraryId }), [libraryId, setLibraryId]);

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
		mainContent = (
			<Routes>
				{/* El lector vive fuera de AppLayout: sin drawer ni barra superior. */}
				<Route path="/read/:mediaId" element={<ReaderPage />} />
				<Route element={<AppLayout />}>
					<Route index element={<HomePage />} />
					<Route path="/series" element={<SeriesGridPage />} />
					<Route path="/series/:id" element={<SeriesDetailPage />} />
					<Route path="/media/:id" element={<MediaDetailPage />} />
					<Route path="/users" element={<UsersPage />} />
					<Route path="/roles" element={<RolesPage />} />
					<Route path="/metadata" element={<MetadataPage />} />
					<Route path="/libraries" element={<LibrariesPage />} />
					<Route path="/libraries/:id" element={<LibraryDetailPage />} />
					<Route path="/upload" element={<UploadPage />} />
					<Route path="*" element={<Navigate to="/" replace />} />
				</Route>
			</Routes>
		);
	}

	return (
		<ColorModeContext.Provider value={colorMode}>
			<ThemeProvider theme={theme}>
				<CssBaseline />
				<SessionContext.Provider value={sessionValue}>
					<LibraryFilterContext.Provider value={libraryFilter}>{mainContent}</LibraryFilterContext.Provider>
				</SessionContext.Provider>
			</ThemeProvider>
		</ColorModeContext.Provider>
	);
}
