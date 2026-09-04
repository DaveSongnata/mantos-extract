<?xml version="1.0"?>
<!--
  MantosExtract Add-on - AppUI.xslt (DOCKER)

  Defines, by appending to the live uiConfig:
    1) a toggle checkButton (guid ce01d45e…) that shows/hides the Mantos Extract docker;
    2) a wpfhost item (guid 30dbb917…) hosting the .NET docker UserControl
       MantosExtract.AddIn.Ui.MantosExtractDocker out of Addons\MantosExtract\MantosExtract.AddIn.dll;
    3) the anchored docker itself (guid 3bebb2f8…) containing that host.

  Structure copied verbatim from optimus/src/Optimus.AddIn/addon/AppUI.xslt (spec §0.1) — only
  the GUIDs, hostedType and icon reference differ. The button is PLACED into the Tools menu +
  Standard toolbar by UserUI.xslt. check="*Docker('<guid>')" is the real CorelDRAW docker-toggle
  syntax; type="wpfhost" + hostedType is the .NET-addon hosting contract. uiConfig is in NO
  namespace, so match patterns are unprefixed. UTF-8, no BOM. Relaunch CorelDRAW holding F8
  after changing this.

  icon="guid://<self>" pulls the mark from MantosExtract.Resources.dll via addon/config.xml
  (resEntry id=this GUID -> icon 101) — PENDING: no real icon shipped yet (Fase 1 placeholder,
  see plans/Phase_1.md "DO NOT WANT"), the button renders blank until the Resources DLL carries
  a real Win32 icon resource.
-->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:frmwrk="Corel Framework Data">
  <xsl:output method="xml" encoding="UTF-8" indent="yes"/>

  <!-- applicationInfo MUST be the topmost frmwrk element; moves our items into user config. -->
  <frmwrk:uiconfig>
    <frmwrk:applicationInfo userConfiguration="true" />
  </frmwrk:uiconfig>

  <!-- Identity transform: copy the existing UI unchanged. -->
  <xsl:template match="node()|@*">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>
    </xsl:copy>
  </xsl:template>

  <!-- 1) the toggle button + 2) the WPF-hosted docker content control. -->
  <xsl:template match="uiConfig/items">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>

      <itemData guid="ce01d45e-4bf8-455b-b452-571fb0c12182"
                type="checkButton"
                check="*Docker('3bebb2f8-92d5-4d89-8dd3-dcc6a8d4bb13')"
                icon="guid://ce01d45e-4bf8-455b-b452-571fb0c12182"
                userCaption="Mantos Extract"
                userToolTip="Mantos Extract &#8212; Extração de elementos de estampa via IA"/>

      <itemData guid="30dbb917-d721-429e-a095-9cf214b5ad97"
                type="wpfhost"
                hostedType="Addons\MantosExtract\MantosExtract.AddIn.dll,MantosExtract.AddIn.Ui.MantosExtractDocker"
                enable="true"/>
    </xsl:copy>
  </xsl:template>

  <!-- 3) the anchored docker, hosting the control (fill the whole docker). -->
  <xsl:template match="uiConfig/dockers">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>
      <dockerData guid="3bebb2f8-92d5-4d89-8dd3-dcc6a8d4bb13"
                  userCaption="Mantos Extract"
                  wantReturn="true">
        <container focusStyle="noThrow">
          <item dock="fill" guidRef="30dbb917-d721-429e-a095-9cf214b5ad97"/>
        </container>
      </dockerData>
    </xsl:copy>
  </xsl:template>

</xsl:stylesheet>
