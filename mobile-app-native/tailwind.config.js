
/** @type {import('tailwindcss').Config} */
module.exports = {
    // NOTE: Update this to include the paths to all of your component files.
    content: ["./App.{js,jsx,ts,tsx}", "./app/**/*.{js,jsx,ts,tsx}", "./src/**/*.{js,jsx,ts,tsx}"],
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
                card: "#111111",
                border: "#262626",
            },
            fontFamily: {
                sans: ["Inter", "sans-serif"],
                heading: ["Rajdhani", "sans-serif"],
            },
        },
    },
    plugins: [],
}
