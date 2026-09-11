import { createContext, useContext } from "react";
import type { PaletteMode } from "@mui/material";

export interface ColorModeContextValue {
	mode: PaletteMode;
	toggle: () => void;
}

export const ColorModeContext = createContext<ColorModeContextValue>({
	mode: "light",
	toggle: () => undefined,
});

export const useColorMode = () => useContext(ColorModeContext);
