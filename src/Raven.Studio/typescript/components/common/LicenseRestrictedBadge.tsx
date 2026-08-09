import Badge from "react-bootstrap/Badge";
import React from "react";
import classNames from "classnames";
import { useAppSelector } from "components/store";
import { licenseSelectors } from "./shell/licenseSlice";

export type LicenseBadgeText = "Professional +" | "Enterprise" | "Enterprise AI";

interface LicenseRestrictedBadgeProps {
    className?: string;
    licenseRequired: LicenseBadgeText;
}

export default function LicenseRestrictedBadge({ className, licenseRequired }: LicenseRestrictedBadgeProps) {
    const isCloud = useAppSelector(licenseSelectors.statusValue("IsCloud"));

    return (
        <Badge
            className={classNames("ms-2 license-restricted-badge", className, getClassName(licenseRequired, isCloud))}
            bg="secondary"
        >
            {isCloud ? "Production" : licenseRequired}
        </Badge>
    );
}

type LicenseClassName = "enterprise" | "professional" | "enterprise-ai";

function getClassName(licenseBadgeText: LicenseBadgeText, isCloud: boolean): LicenseClassName {
    if (isCloud) {
        return "enterprise";
    }

    switch (licenseBadgeText) {
        case "Enterprise":
            return "enterprise";
        case "Professional +":
            return "professional";
        case "Enterprise AI":
            return "enterprise-ai";
        default:
            return null;
    }
}
