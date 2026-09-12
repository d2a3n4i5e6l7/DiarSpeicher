import { useEffect, useState } from "react";
import { Alert, Box, LinearProgress, Stack, Typography } from "@mui/material";
import FolderIcon from "@mui/icons-material/Folder";
import { filesystemApi, type DeletionPreview } from "../api/endpoints";
import { DS } from "../theme";
import { formatBytes } from "../catalog/bytes";

/**
 * Lo que se va a destruir, contado por el servidor sobre el disco real.
 *
 * La cuenta no la hace el cliente a ojo: el aviso de un borrado irreversible solo vale si
 * el numero es el de verdad.
 */
export default function DeleteScopeNotice({ path }: Readonly<{ path: string }>) {
	const [scope, setScope] = useState<{ key: string; data: DeletionPreview | null; error: string | null } | null>(null);

	useEffect(() => {
		if (!path) return;

		let cancelled = false;
		filesystemApi.deletionPreview(path).then(
			(data) => {
				if (!cancelled) setScope({ key: path, data, error: null });
			},
			(e: unknown) => {
				if (!cancelled) {
					setScope({ key: path, data: null, error: e instanceof Error ? e.message : "No se pudo leer la carpeta." });
				}
			},
		);

		return () => {
			cancelled = true;
		};
	}, [path]);

	const fresh = scope?.key === path ? scope : null;

	if (!fresh) return <LinearProgress sx={{ height: 2, mt: 2 }} />;

	if (fresh.error ?? !fresh.data) {
		return (
			<Alert severity="error" sx={{ mt: 2 }}>
				{fresh.error ?? "No se pudo leer la carpeta."}
			</Alert>
		);
	}

	const data = fresh.data;

	return (
		<Alert severity="error" sx={{ mt: 2 }}>
			<Typography sx={{ fontWeight: 700, mb: 1 }}>
				Se borrarán {data.fileCount} archivos del disco ({formatBytes(data.bytes)}).
			</Typography>

			{data.folders.length > 0 && (
				<Box sx={{ maxHeight: 160, overflowY: "auto", mb: 1 }}>
					{data.folders.map((folder) => (
						<Stack
							key={folder.path}
							direction="row"
							spacing={1}
							sx={{ alignItems: "center", py: 0.3, borderBottom: `1px solid ${DS.borderSoft}` }}
						>
							<FolderIcon sx={{ fontSize: 14, color: DS.redLight, flexShrink: 0 }} />
							<Typography noWrap sx={{ flexGrow: 1, minWidth: 0, fontSize: "12px" }}>
								{folder.name}
							</Typography>
							<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "11px", flexShrink: 0 }}>
								{folder.fileCount}
							</Typography>
						</Stack>
					))}
				</Box>
			)}

			<Typography sx={{ fontSize: "12px" }}>
				Se podrá recuperar desde <strong>Restaurar</strong> durante una hora. Después, no.
			</Typography>
		</Alert>
	);
}
