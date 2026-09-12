import { Box } from "@mui/material";

/**
 * Los cuatro marcadores de esquina del HUD. El contenedor que lo envuelve debe
 * tener `position: relative` (las tarjetas y dialogos ya lo traen del tema).
 */
export default function HudFrame() {
	return (
		<>
			<Box className="hud-corner-tl" />
			<Box className="hud-corner-tr" />
			<Box className="hud-corner-bl" />
			<Box className="hud-corner-br" />
		</>
	);
}
