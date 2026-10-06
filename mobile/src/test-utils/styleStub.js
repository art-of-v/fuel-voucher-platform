// Stand-in for a stylesheet import under Jest.
//
// The root layout imports `global.css` (NativeWind's Tailwind entry), and Jest cannot
// parse CSS — without this map, importing the layout fails at parse time before a
// single assertion runs. No component reads a class name at runtime, so an empty
// object is a faithful stand-in.
module.exports = {};
