import { Box, Menu, MenuItem, Tooltip, Typography } from "@mui/material";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";
import StorageIcon from "@mui/icons-material/Storage";
import { useEffect, useState } from "react";
import { librariesApi, type LibraryItem } from "../api/endpoints";
import { useLibraryFilter } from "../catalog/LibraryFilterContext";
import { DS } from "../theme";

const ALL_LABEL = "TODAS";

/** Filtro de sector activo. Vive en la barra superior, no en el drawer: no es navegación. */
export default function LibrarySelector() {
	const { libraryId, setLibraryId } = useLibraryFilter();
	const [libraries, setLibraries] = useState<LibraryItem[]>([]);
	const [anchor, setAnchor] = useState<HTMLElement | null>(null);

	useEffect(() => {
		let mounted = true;
		librariesApi
			.list()
			.then((list) => {
				if (mounted) setLibraries(list || []);
			})
			.catch(() => {
				// Sin catálogo de bibliotecas el filtro se queda en "todas", que es lo correcto.
			});
		return () => {
			mounted = false;
		};
	}, []);

	// Si la biblioteca guardada ya no existe (se borró desde otra sesión), volver a "todas".
	useEffect(() => {
		if (libraryId && libraries.length > 0 && !libraries.some((lib) => lib.id === libraryId)) {
			setLibraryId(null);
		}
	}, [libraryId, libraries, setLibraryId]);

	const selected = libraries.find((lib) => lib.id === libraryId);
	const label = selected?.name.toUpperCase() ?? ALL_LABEL;

	const choose = (next: string | null) => {
		setLibraryId(next);
		setAnchor(null);
	};

	return (
		<>
			<Tooltip title="Filtrar por biblioteca">
				<Box
					component="button"
					type="button"
					onClick={(e: React.MouseEvent<HTMLElement>) => setAnchor(e.currentTarget)}
					sx={{
						display: { xs: "none", sm: "inline-flex" },
						alignItems: "center",
						gap: 0.75,
						cursor: "pointer",
						background: DS.bgSunken,
						border: `1px solid ${libraryId ? DS.red : DS.border}`,
						color: libraryId ? DS.redGlow : "#A3ABB8",
						fontFamily: "'Rajdhani', sans-serif",
						fontWeight: 700,
						fontSize: "13px",
						letterSpacing: "1px",
						textTransform: "uppercase",
						px: 1.25,
						py: 0.5,
						mr: 1.5,
						maxWidth: 220,
						clipPath: "polygon(6px 0%, 100% 0%, 100% calc(100% - 6px), calc(100% - 6px) 100%, 0% 100%, 0% 6px)",
						"&:hover": { borderColor: DS.redGlow, color: "#FFFFFF" },
					}}
				>
					<StorageIcon sx={{ fontSize: 16 }} />
					<Box component="span" sx={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
						{label}
					</Box>
					<ExpandMoreIcon sx={{ fontSize: 16 }} />
				</Box>
			</Tooltip>

			<Menu anchorEl={anchor} open={Boolean(anchor)} onClose={() => setAnchor(null)}>
				<MenuItem selected={libraryId === null} onClick={() => choose(null)}>
					<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", fontWeight: 700, letterSpacing: "1px" }}>
						{ALL_LABEL}
					</Typography>
				</MenuItem>
				{libraries.map((lib) => (
					<MenuItem key={lib.id} selected={lib.id === libraryId} onClick={() => choose(lib.id)}>
						<Box>
							<Typography sx={{ fontFamily: "'Rajdhani', sans-serif", fontWeight: 700, letterSpacing: "1px" }}>
								{lib.name.toUpperCase()}
							</Typography>
							<Typography sx={{ fontFamily: "'JetBrains Mono', monospace", fontSize: "10px", color: DS.subtle }}>
								{String(lib.seriesCount ?? 0)} SERIES · {String(lib.mediaCount ?? 0)} TOMOS
							</Typography>
						</Box>
					</MenuItem>
				))}
			</Menu>
		</>
	);
}
