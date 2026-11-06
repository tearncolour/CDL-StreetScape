// The official CSII UI template's webpack config (npx create-csii-ui-mod), adapted in three places, each marked "CDLC":
// the mod's id, author, version and dependencies come from the repository's own mod.json (one copy, not two); the output goes to UI/build or to CDLC_UI_OUT, never to the
// game's Mods folder (the template's default output is "<user data>\Mods\<id>", which would stand a second copy of this mod next to the subscribed one); our plain CSS
// is taken as it is (no sass, no url resolving, no hashing of class names: the script uses the literal cdlts-* names).
const path = require("path");
const MOD = require("../mod.json");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");
const { CSSPresencePlugin } = require("./tools/css-presence");
const TerserPlugin = require("terser-webpack-plugin");
const gray = (text) => `\x1b[90m${text}\x1b[0m`;

// CDLC: the output folder.
const OUTPUT_DIR = path.resolve(__dirname, process.env.CDLC_UI_OUT || "build");
const MODS_FOLDER = process.env.CSII_USERDATAPATH ? path.resolve(process.env.CSII_USERDATAPATH, "Mods") : null;
if (MODS_FOLDER && (OUTPUT_DIR.toLowerCase() + path.sep).startsWith(MODS_FOLDER.toLowerCase() + path.sep)) {
  throw `CDLC_UI_OUT is inside the game's Mods folder (${MODS_FOLDER}): the build is never written there`;
}

const banner = `
 * Cities: Skylines II UI Module
 *
 * Id: ${MOD.id}
 * Author: ${MOD.author}
 * Version: ${MOD.version}
 * Dependencies: ${MOD.dependencies.join(",")}
`;

module.exports = {
  mode: "production",
  stats: "none",
  entry: {
    [MOD.id]: "./src/index.tsx",
  },
  externalsType: "window",
  externals: {
    react: "React",
    "react-dom": "ReactDOM",
    "cs2/modding": "cs2/modding",
    "cs2/api": "cs2/api",
    "cs2/bindings": "cs2/bindings",
    "cs2/l10n": "cs2/l10n",
    "cs2/ui": "cs2/ui",
    "cs2/input": "cs2/input",
    "cs2/utils": "cs2/utils",
    "cohtml/cohtml": "cohtml/cohtml",
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: "ts-loader",
        exclude: /node_modules/,
      },
      // CDLC: the mod's own stylesheet, plain CSS written for the game's CSS (cohtml): taken as it is.
      {
        test: /\.css$/,
        include: path.join(__dirname, "src"),
        use: [
          MiniCssExtractPlugin.loader,
          {
            loader: "css-loader",
            options: {
              url: false,
              import: false,
              modules: false,
            },
          },
        ],
      },
      {
        test: /\.scss$/,
        include: path.join(__dirname, "src"),
        use: [
          MiniCssExtractPlugin.loader,
          {
            loader: "css-loader",
            options: {
              url: true,
              importLoaders: 1,
              modules: {
                auto: true,
                exportLocalsConvention: "camelCase",
                localIdentName: "[local]_[hash:base64:3]",
              },
            },
          },
          "sass-loader",
        ],
      },
      {
        test: /\.(png|jpe?g|gif|svg)$/i,
        type: "asset/resource",
        generator: {
          filename: "images/[name][ext][query]",
        },
      },
    ],
  },
  resolve: {
    extensions: [".tsx", ".ts", ".js"],
    modules: ["node_modules", path.join(__dirname, "src")],
    alias: {
      "mod.json": path.resolve(__dirname, "../mod.json"),
    },
  },
  output: {
    path: OUTPUT_DIR,
    library: {
      type: "module",
    },
    publicPath: `coui://ui-mods/`,
  },
  optimization: {
    minimize: true,
    minimizer: [
      new TerserPlugin({
        // CDLC: the components keep their names (the offline tests tell a component by its name, and the game's error stacks name it).
        terserOptions: { keep_fnames: true, keep_classnames: true },
        extractComments: {
          banner: () => banner,
        },
      }),
    ],
  },
  experiments: {
    outputModule: true,
  },
  plugins: [
    new MiniCssExtractPlugin(),
    new CSSPresencePlugin(),
    {
      apply(compiler) {
        let runCount = 0;
        compiler.hooks.done.tap("AfterDonePlugin", (stats) => {
          console.log(stats.toString({ colors: true }));
          console.log(`\n🔨 ${!runCount++ ? "Built" : "Updated"} ${MOD.id}`);
          console.log("   " + gray(OUTPUT_DIR) + "\n");
        });
      },
    },
  ],
};
