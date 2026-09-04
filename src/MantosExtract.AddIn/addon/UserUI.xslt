<?xml version="1.0"?>
<!--
  MantosExtract Add-on - UserUI.xslt
  PLACES the Mantos Extract button (defined in AppUI.xslt as itemData guid ce01d45e-…) into
  two always-visible containers. Applied once per workspace; after editing it, launch
  CorelDRAW holding F8 to force a workspace reset (back up the Workspace folder first).

  Target GUIDs (Tools menu / Standard toolbar / separator) copied from
  optimus/src/Optimus.AddIn/addon/UserUI.xslt — they are CorelDRAW's own live DrawUI.xml
  containers, in NO namespace:
    Tools menu       : commandBarData guid 6f114d89-1b8c-4877-a4af-a3624ddd95f6  (child <menu>)
    Standard toolbar : commandBarData guid c2b44f69-6dec-444e-a37e-5dbf7ff43dae  (child <toolbar>)
    Separator item   : guidRef 266435b4-6e53-460f-9fa7-f45be187d400
    MantosExtract item : guidRef ce01d45e-4bf8-455b-b452-571fb0c12182  (ours; from AppUI.xslt)

  Both placements are IDEMPOTENT (xsl:if test="not(...)") so an F8 re-apply never duplicates.
-->
<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:frmwrk="Corel Framework Data"
                exclude-result-prefixes="frmwrk">
  <xsl:output method="xml" encoding="UTF-8" indent="yes"/>

  <frmwrk:uiconfig>
    <frmwrk:applicationInfo userConfiguration="true" />
    <frmwrk:compositeNode xPath="/uiConfig/commandBars/commandBarData[@guid='6f114d89-1b8c-4877-a4af-a3624ddd95f6']"/>
    <frmwrk:compositeNode xPath="/uiConfig/commandBars/commandBarData[@guid='c2b44f69-6dec-444e-a37e-5dbf7ff43dae']"/>
    <frmwrk:compositeNode xPath="/uiConfig/frame"/>
  </frmwrk:uiconfig>

  <!-- Identity transform: copy all of the existing user interface unchanged. -->
  <xsl:template match="node()|@*">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>
    </xsl:copy>
  </xsl:template>

  <!-- Tools menu (Ferramentas): append a separator + the MantosExtract button at the end. -->
  <xsl:template match="uiConfig/commandBars/commandBarData[@guid='6f114d89-1b8c-4877-a4af-a3624ddd95f6']/menu">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>
      <xsl:if test="not(./item[@guidRef='ce01d45e-4bf8-455b-b452-571fb0c12182'])">
        <item guidRef="266435b4-6e53-460f-9fa7-f45be187d400"/> <!-- separator -->
        <item guidRef="ce01d45e-4bf8-455b-b452-571fb0c12182"/> <!-- the MantosExtract button -->
      </xsl:if>
    </xsl:copy>
  </xsl:template>

  <!-- Standard toolbar: append a separator + the MantosExtract button at the end. -->
  <xsl:template match="uiConfig/commandBars/commandBarData[@guid='c2b44f69-6dec-444e-a37e-5dbf7ff43dae']/toolbar">
    <xsl:copy>
      <xsl:apply-templates select="node()|@*"/>
      <xsl:if test="not(./item[@guidRef='ce01d45e-4bf8-455b-b452-571fb0c12182'])">
        <item guidRef="266435b4-6e53-460f-9fa7-f45be187d400"/> <!-- separator -->
        <item guidRef="ce01d45e-4bf8-455b-b452-571fb0c12182"/> <!-- the MantosExtract button -->
      </xsl:if>
    </xsl:copy>
  </xsl:template>

</xsl:stylesheet>
