/** @type {import('next').NextConfig} */
const nextConfig = {
  webpack: (config, { isServer }) => {
    // Add support for canvas and image processing libraries
    if (!isServer) {
      config.resolve.fallback = {
        ...config.resolve.fallback,
        canvas: false,
        encoding: false,
      };
    }
    return config;
  },
  images: {
    domains: ["localhost"],
    formats: ["image/webp", "image/avif"],
  },
  module.exports = {
    output: 'standalone'
  };
};

export default nextConfig;
