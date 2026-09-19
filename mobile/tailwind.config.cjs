/** @type {import('tailwindcss').Config} */
module.exports = {
    content: ["./app/**/*.{js,jsx,ts,tsx}", "./src/**/*.{js,jsx,ts,tsx}"],
    presets: [require("nativewind/preset")],
    theme: {
        extend: {
            colors: {
                primary: {
                    DEFAULT: "#00FF80",
                    foreground: "#000000",
                },
                secondary: {
                    DEFAULT: "#1F1F1F",
                    foreground: "#FAFAFA",
                },
                accent: {
                    DEFAULT: "#00FFFF",
                    foreground: "#000000",
                },
                background: "#050505",
                foreground: "#FAFAFA",
                card: "rgba(0, 0, 0, 0.8)",
                border: "rgba(255, 255, 255, 0.1)",
            },
            fontFamily: {
                sans: ["Inter", "sans-serif"],
                heading: ["Rajdhani", "sans-serif"],
                mono: ["SpaceMono", "monospace"],
            },
            borderRadius: {
                'xs': '2px',
                'DEFAULT': '8px',
            }
        },
    },
    plugins: [],
}
