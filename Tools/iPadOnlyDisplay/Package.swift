// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "iPadOnlyDisplay",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "iPadOnlyDisplay", targets: ["iPadOnlyDisplay"])
    ],
    targets: [
        .executableTarget(
            name: "iPadOnlyDisplay",
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("Carbon")
            ]
        )
    ]
)
