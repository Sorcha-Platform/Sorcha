// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "SorchaProximity",
    platforms: [.iOS(.v14)],
    products: [
        .library(
            name: "SorchaProximity",
            targets: ["SorchaProximityPlugin"])
    ],
    dependencies: [
        .package(url: "https://github.com/ionic-team/capacitor-swift-pm.git", from: "8.4.0")
    ],
    targets: [
        .target(
            name: "SorchaProximityPlugin",
            dependencies: [
                .product(name: "Capacitor", package: "capacitor-swift-pm"),
                .product(name: "Cordova", package: "capacitor-swift-pm")
            ],
            path: "ios/Sources/SorchaProximity"),
        .testTarget(
            name: "SorchaProximityPluginTests",
            dependencies: ["SorchaProximityPlugin"],
            path: "ios/Tests/SorchaProximityPluginTests")
    ]
)
