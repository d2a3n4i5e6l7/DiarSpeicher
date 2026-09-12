export type NavSection = "content" | "admin";

export interface NavItem {
	path: string;
	label: string;
	icon: string;
	section: NavSection;
	/** Solo la ruta raíz debe casar exacta: si no, "/" marcaría todas las entradas. */
	exact?: boolean;
}

export interface NavGroup {
	section: NavSection;
	title: string;
	items: NavItem[];
}

export const NAV_GROUPS: NavGroup[] = [
	{
		section: "content",
		title: "// ARCHIVO",
		items: [
			{ path: "/", label: "Inicio", icon: "Home", section: "content", exact: true },
			{ path: "/series", label: "Series", icon: "CollectionsBookmark", section: "content" },
		],
	},
	{
		section: "admin",
		title: "// SECTOR CONTROL",
		items: [
			{ path: "/libraries", label: "Bibliotecas", icon: "LibraryBooks", section: "admin" },
			{ path: "/upload", label: "Subida de Ficheros", icon: "CloudUpload", section: "admin" },
			{ path: "/users", label: "Usuarios del Reino", icon: "People", section: "admin" },
			{ path: "/roles", label: "Roles y Permisos", icon: "AdminPanelSettings", section: "admin" },
			{ path: "/metadata", label: "Metadata Externa", icon: "Storage", section: "admin" },
		],
	},
];

export const NAV_ITEMS: NavItem[] = NAV_GROUPS.flatMap((group) => group.items);

export function isNavItemActive(item: NavItem, pathname: string): boolean {
	return item.exact ? pathname === item.path : pathname.startsWith(item.path);
}
