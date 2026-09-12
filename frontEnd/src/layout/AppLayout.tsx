import {
	AppBar,
	Box,
	Chip,
	Divider,
	Drawer,
	IconButton,
	List,
	ListItemButton,
	ListItemIcon,
	ListItemText,
	Toolbar,
	Tooltip,
	Typography,
	useMediaQuery,
	useTheme,
} from "@mui/material";
import PeopleIcon from "@mui/icons-material/People";
import AdminPanelSettingsIcon from "@mui/icons-material/AdminPanelSettings";
import RestoreIcon from "@mui/icons-material/Restore";
import LibraryBooksIcon from "@mui/icons-material/LibraryBooks";
import HomeIcon from "@mui/icons-material/Home";
import CollectionsBookmarkIcon from "@mui/icons-material/CollectionsBookmark";
import StorageIcon from "@mui/icons-material/Storage";
import LogoutIcon from "@mui/icons-material/Logout";
import MenuIcon from "@mui/icons-material/Menu";
import DarkModeIcon from "@mui/icons-material/DarkMode";
import LightModeIcon from "@mui/icons-material/LightMode";
import React, { useState } from "react";
import { NavLink, Outlet, useLocation } from "react-router-dom";
import { NAV_GROUPS, NAV_ITEMS, isNavItemActive } from "./navItems";
import LibrarySelector from "./LibrarySelector";
import { useColorMode } from "../theme/ColorModeContext";
import { useSession } from "../auth/SessionContext";
import { URL_ACCESS } from "../api/client";
import DiarSpeicherLogo from "../components/DiarSpeicherLogo";

const DRAWER_WIDTH = 250;

const ICONS: Record<string, typeof PeopleIcon> = {
	People: PeopleIcon,
	AdminPanelSettings: AdminPanelSettingsIcon,
	Restore: RestoreIcon,
	LibraryBooks: LibraryBooksIcon,
	Home: HomeIcon,
	CollectionsBookmark: CollectionsBookmarkIcon,
	Storage: StorageIcon,
};

export default function AppLayout() {
	const theme = useTheme();
	const isDesktop = useMediaQuery(theme.breakpoints.up("md"));
	const [mobileOpen, setMobileOpen] = useState(false);
	const { mode, toggle } = useColorMode();
	const { user, logout } = useSession();
	const location = useLocation();

	// La coincidencia más larga gana: con "/" en la lista, startsWith casaría siempre con ella.
	const current = [...NAV_ITEMS]
		.sort((a, b) => b.path.length - a.path.length)
		.find((item) => isNavItemActive(item, location.pathname));
	const currentTitle = current?.label ?? "Archivo DiarSpeicher";

	let userBadge: React.ReactNode = null;
	if (user) {
		const rolePrefix = user.is_admin ? "SYSADMIN" : "READER";
		userBadge = (
			<Chip
				label={`${rolePrefix} // ${user.username}`}
				size="small"
				sx={{
					fontFamily: "'JetBrains Mono', monospace",
					fontSize: "11px",
					fontWeight: 700,
					backgroundColor: "var(--ds-bg-panel)",
					color: user.is_admin ? "var(--ds-red-glow)" : "var(--ds-platinum)",
					border: `1px solid ${user.is_admin ? "var(--ds-red)" : "var(--ds-border)"}`,
					borderRadius: "2px",
					mr: 1.5,
					px: 0.5,
				}}
			/>
		);
	}

	let themeToggleIcon = <LightModeIcon fontSize="small" />;
	let themeTooltip = "Tema claro";
	if (mode === "light") {
		themeToggleIcon = <DarkModeIcon fontSize="small" />;
		themeTooltip = "Tema oscuro táctico";
	}

	const drawerContent = (
		<Box
			sx={{
				display: "flex",
				flexDirection: "column",
				height: "100%",
				backgroundColor: "var(--ds-bg-sunken)",
				color: "var(--ds-platinum)",
			}}
		>
			<Toolbar
				sx={{
					px: 2.5,
					borderBottom: "1px solid var(--ds-border-head)",
					display: "flex",
					alignItems: "center",
					justifyContent: "flex-start",
					minHeight: "64px !important",
				}}
			>
				<DiarSpeicherLogo size={32} showSubtitle subtitle="ARCHIVE PROTOCOL" />
			</Toolbar>

			<Box sx={{ flexGrow: 1, overflowY: "auto" }}>
				{NAV_GROUPS.map((group) => (
					<Box key={group.section}>
						<Box sx={{ px: 2, pt: 2.5, pb: 1 }}>
							<Typography
								sx={{
									fontFamily: "'Rajdhani', sans-serif",
									fontWeight: 700,
									fontSize: "11px",
									letterSpacing: "2px",
									color: "var(--ds-subtle)",
									textTransform: "uppercase",
								}}
							>
								{group.title}
							</Typography>
						</Box>

						<List sx={{ px: 1.5, py: 0.5 }}>
							{group.items.map((item) => {
								const Icon = ICONS[item.icon];
								const isSelected = isNavItemActive(item, location.pathname);

								return (
									<ListItemButton
										key={item.path}
										component={NavLink}
										to={item.path}
										selected={isSelected}
										onClick={() => setMobileOpen(false)}
										sx={{
											borderRadius: "2px",
											mb: 0.75,
											py: 1,
											px: 1.5,
											transition: "all 0.15s ease",
											borderLeft: isSelected ? "3px solid var(--ds-red)" : "3px solid transparent",
											backgroundColor: isSelected ? "rgba(var(--ds-red-rgb), 0.12) !important" : "transparent",
											color: isSelected ? "var(--ds-text-strong)" : "var(--ds-text-2)",
											"&:hover": {
												backgroundColor: "rgba(var(--ds-red-rgb), 0.08)",
												color: "var(--ds-text-strong)",
												"& .MuiListItemIcon-root": {
													color: "var(--ds-red-glow)",
												},
											},
										}}
									>
										<ListItemIcon
											sx={{
												minWidth: 36,
												color: isSelected ? "var(--ds-red-glow)" : "var(--ds-subtle)",
												transition: "color 0.15s ease",
											}}
										>
											<Icon fontSize="small" />
										</ListItemIcon>
										<ListItemText
											primary={item.label}
											slotProps={{
												primary: {
													sx: {
														fontFamily: "'Rajdhani', sans-serif",
														fontWeight: isSelected ? 700 : 600,
														fontSize: "14px",
														letterSpacing: "0.5px",
														textTransform: "uppercase",
													},
												},
											}}
										/>
									</ListItemButton>
								);
							})}
						</List>
					</Box>
				))}
			</Box>

			<Divider sx={{ borderColor: "var(--ds-border-soft)" }} />

			<Box sx={{ p: 2, backgroundColor: "var(--ds-bg-deep)" }}>
				<Box sx={{ display: "flex", alignItems: "center", gap: 1, mb: 0.5 }}>
					<Box
						sx={{
							width: 6,
							height: 6,
							borderRadius: "50%",
							backgroundColor: "var(--ds-ok)",
							boxShadow: "0 0 6px var(--ds-ok)",
						}}
					/>
					<Typography
						sx={{
							fontFamily: "'JetBrains Mono', monospace",
							fontSize: "10px",
							color: "var(--ds-muted)",
						}}
					>
						NODE: ONLINE
					</Typography>
				</Box>
				<Typography
					variant="caption"
					sx={{
						fontFamily: "'JetBrains Mono', monospace",
						fontSize: "10px",
						color: "var(--ds-subtle)",
						display: "block",
						overflow: "hidden",
						textOverflow: "ellipsis",
						whiteSpace: "nowrap",
					}}
				>
					GW: <code>{URL_ACCESS}</code>
				</Typography>
			</Box>
		</Box>
	);

	return (
		<Box sx={{ display: "flex", height: "100vh", overflow: "hidden", backgroundColor: "var(--ds-bg)" }}>
			<AppBar
				position="fixed"
				elevation={0}
				sx={{
					borderBottom: "1px solid var(--ds-border-head)",
					background: "linear-gradient(90deg, var(--ds-red-deep) 0%, var(--ds-bg) 100%)",
					width: { md: `calc(100% - ${DRAWER_WIDTH}px)` },
					ml: { md: `${DRAWER_WIDTH}px` },
					zIndex: (appBarTheme) => appBarTheme.zIndex.drawer + 1,
				}}
			>
				<Toolbar sx={{ minHeight: "64px !important", px: { xs: 2, sm: 3 } }}>
					{!isDesktop && (
						<IconButton
							edge="start"
							onClick={() => setMobileOpen(true)}
							sx={{ mr: 1.5, color: "var(--ds-platinum)" }}
						>
							<MenuIcon />
						</IconButton>
					)}

					<Box sx={{ flexGrow: 1, display: "flex", alignItems: "center", gap: 1.5 }}>
						<Typography
							variant="subtitle1"
							sx={{
								fontFamily: "'Rajdhani', sans-serif",
								fontWeight: 700,
								fontSize: "18px",
								letterSpacing: "1.5px",
								textTransform: "uppercase",
								color: "var(--ds-text-strong)",
							}}
						>
							{currentTitle}
						</Typography>
						<Box
							sx={{
								px: 1,
								py: 0.25,
								backgroundColor: "var(--ds-bg-panel)",
								border: "1px solid var(--ds-border)",
								borderRadius: "2px",
								display: { xs: "none", sm: "block" },
							}}
						>
							<Typography
								sx={{
									fontFamily: "'JetBrains Mono', monospace",
									fontSize: "10px",
									color: "var(--ds-muted)",
								}}
							>
								DS-NODE-01
							</Typography>
						</Box>
					</Box>

					<LibrarySelector />

					{userBadge}

					<Tooltip title={themeTooltip}>
						<IconButton
							onClick={toggle}
							sx={{
								color: "var(--ds-text-2)",
								border: "1px solid var(--ds-border)",
								borderRadius: "2px",
								p: 0.75,
								mr: 1,
								"&:hover": { color: "var(--ds-text-strong)", borderColor: "var(--ds-red)" },
							}}
						>
							{themeToggleIcon}
						</IconButton>
					</Tooltip>

					<Tooltip title="Cerrar sesión de nodo">
						<IconButton
							id="logout-btn"
							onClick={() => {
								void logout();
							}}
							sx={{
								color: "var(--ds-text-2)",
								border: "1px solid var(--ds-border)",
								borderRadius: "2px",
								p: 0.75,
								"&:hover": { color: "var(--ds-red-glow)", borderColor: "var(--ds-red)", backgroundColor: "rgba(var(--ds-red-rgb), 0.1)" },
							}}
						>
							<LogoutIcon fontSize="small" />
						</IconButton>
					</Tooltip>
				</Toolbar>
			</AppBar>

			<Box component="nav" sx={{ width: { md: DRAWER_WIDTH }, flexShrink: { md: 0 } }}>
				<Drawer
					variant={isDesktop ? "permanent" : "temporary"}
					open={isDesktop || mobileOpen}
					onClose={() => setMobileOpen(false)}
					ModalProps={{ keepMounted: true }}
					sx={{
						"& .MuiDrawer-paper": {
							width: DRAWER_WIDTH,
							boxSizing: "border-box",
							borderRight: "1px solid var(--ds-border-head)",
						},
					}}
				>
					{drawerContent}
				</Drawer>
			</Box>

			<Box
				component="main"
				sx={{
					flexGrow: 1,
					minWidth: 0,
					maxWidth: { xs: "100%", md: `calc(100% - ${DRAWER_WIDTH}px)` },
					height: "100vh",
					display: "flex",
					flexDirection: "column",
					overflow: "hidden",
					position: "relative",
					backgroundColor: "var(--ds-bg)",
					/* Rejilla tactica y halo carmesi: fijos, no arrastran con el scroll. */
					"&::before": {
						content: '""',
						position: "absolute",
						inset: 0,
						backgroundImage:
							"linear-gradient(to right, var(--ds-grid-line) 1px, transparent 1px), linear-gradient(to bottom, var(--ds-grid-line) 1px, transparent 1px)",
						backgroundSize: "40px 40px",
						pointerEvents: "none",
						zIndex: 0,
					},
					/* El halo se cierra en el mismo rojo con alfa 0, no en `transparent`:
					 * `transparent` es rgba(0,0,0,0) y al interpolar deja un aro gris
					 * que Firefox, que no aplica dithering, dibuja como un circulo.
					 * La capa de ruido remata las bandas que queden. */
					"&::after": {
						content: '""',
						position: "absolute",
						inset: 0,
						backgroundImage:
							"radial-gradient(ellipse 130% 80% at 50% -20%, rgba(var(--ds-red-rgb), 0.10) 0%, rgba(var(--ds-red-rgb), 0.065) 26%, rgba(var(--ds-red-rgb), 0.035) 48%, rgba(var(--ds-red-rgb), 0.014) 68%, rgba(var(--ds-red-rgb), 0) 100%)",
						pointerEvents: "none",
						zIndex: 0,
					},
				}}
			>
				<Box className="ds-noise" />
				<Toolbar sx={{ minHeight: "64px !important" }} />
				<Box
					sx={{
						flex: 1,
						minHeight: 0,
						minWidth: 0,
						overflowY: "auto",
						overflowX: "hidden",
						position: "relative",
						zIndex: 1,
						p: { xs: 2, sm: 3.5 },
					}}
				>
					<Outlet />
				</Box>
			</Box>
		</Box>
	);
}
