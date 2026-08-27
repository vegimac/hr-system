<?xml version="1.0" encoding="UTF-8"?>

<xsl:stylesheet version="1.0"
                xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:bcd="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB"

                xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/"
                exclude-result-prefixes="bcd"
>
    <!-- state of standardisation: FINAL
         publication: 2020-05-08
         t. koch
    -->

    <!-- INPUT is _one_ sd:Person -->

    <xsl:output indent="no" method="xml"/>
    <xsl:strip-space elements="*"/>

    <!-- switch TaxAnnuity, TaxSalary or OwnershipRightDetail -->
    <xsl:param name="BarcodeType" select="'TaxSalary'"/>
    <xsl:param name="TaxIndex" select="1"/>
    <xsl:param name="ORDIndex" select="1"/>
    <xsl:param name="SID" select="1"/>
    <xsl:param name="SysV" select="1"/>
    <xsl:param name="verbose" select="'false'"/>

    <!-- company data as input parameters -->
    <xsl:param name="Uid"/>
    <xsl:param name="Hrrc"/>
    <xsl:param name="Zip"/>
    <xsl:param name="CLCompany"/>    
    <xsl:param name="ContactPerson"/>
    <xsl:param name="ContactStreet"/>
    <xsl:param name="ContactPostbox"/>
    <xsl:param name="ContactLocality"/>
    <xsl:param name="ContactCity"/>
    <xsl:param name="ContactCountry"/>
    <xsl:param name="ContactPhone"/>
    
    <xsl:template match="//soapenv:Header"/>

    <!--
        Main template that starts the process
        -->

    <xsl:template match="/">
        <xsl:call-template name="printMessage">
            <xsl:with-param name="message" select="'[TaxAccountingBcdTrans.xsl:Info] start template / ...'"/>
        </xsl:call-template>

        <xsl:apply-templates select="//*[local-name()='Person']"/>
        <xsl:call-template name="printMessage">
            <xsl:with-param name="message" select="'[Info] ... end template /'"/>
        </xsl:call-template>
    </xsl:template>

    <xsl:template match="*[local-name()='Person']">
        <xsl:call-template name="printMessage">
            <xsl:with-param name="message" select="'Extract Barcode Data'"/>
        </xsl:call-template>

        <xsl:element name="T" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:attribute name="SID">
                <xsl:value-of select="$SID"/>
            </xsl:attribute>
            <xsl:attribute name="SysV">
                <xsl:value-of select="$SysV"/>
            </xsl:attribute>

            <xsl:call-template name="getCompany"/>

            <xsl:call-template name="getPersonID"/>

            <xsl:choose>
                <xsl:when test="$BarcodeType = 'TaxSalary'">
                    <xsl:call-template name="S"/>
                </xsl:when>
                <xsl:when test="$BarcodeType = 'TaxAnnuity'">
                    <xsl:call-template name="A"/>
                </xsl:when>
                <xsl:when test="$BarcodeType = 'FormularA'">
                    <xsl:call-template name="OA"/>
                </xsl:when>
                <xsl:when test="$BarcodeType = 'FormularB'">
                    <xsl:call-template name="OB"/>
                </xsl:when>
                <xsl:when test="$BarcodeType = 'FormularC'">
                    <xsl:call-template name="OC"/>
                </xsl:when>
            </xsl:choose>
        </xsl:element>
    </xsl:template>

    <!--
        Copy TaxSalary of the instance document into a new namespace: .../SalaryDeclarationTxAB
    -->

    <xsl:template name="S">
        <xsl:element name="S" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:apply-templates select="*[local-name()='TaxSalaries']/*[local-name()='TaxSalary'][$TaxIndex]/*"/>
        </xsl:element>
    </xsl:template>
    <xsl:template match="*[local-name()='TaxSalaries']/*[local-name()='TaxSalary']//*">
        <xsl:element name="{local-name()}" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:apply-templates/>
        </xsl:element>
    </xsl:template>

    <!--
        Copy TaxAnnuity of the instance document into a new namespace: .../SalaryDeclarationTxAB
    -->

    <xsl:template name="A">
        <xsl:element name="A" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:apply-templates select="*[local-name()='TaxAnnuities']/*[local-name()='TaxAnnuity'][$TaxIndex]/*"/>
        </xsl:element>
    </xsl:template>
    <xsl:template match="*[local-name()='TaxAnnuities']/*[local-name()='TaxAnnuity']//*">
        <xsl:element name="{local-name()}" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:apply-templates/>
        </xsl:element>
    </xsl:template>
    
    <!--
        OwnershipRightDetail
    -->
    <!-- Formular A -->
    <xsl:template name="OA">
        <xsl:element name="OA" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:apply-templates select="*[local-name()='OwnershipRightDetails']/*[local-name()='OwnershipRightDetail'][$TaxIndex]/*[local-name()='FormularA'][$ORDIndex]/*"/>
        </xsl:element>
    </xsl:template>
    <xsl:template match="*[local-name()='FormularA']//*">
        <xsl:element name="{local-name()}" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:apply-templates/>
        </xsl:element>
    </xsl:template>

    <!-- Formular B -->
    <xsl:template name="OB">
        <xsl:element name="OB" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:apply-templates select="*[local-name()='OwnershipRightDetails']/*[local-name()='OwnershipRightDetail'][$TaxIndex]/*[local-name()='FormularB'][$ORDIndex]/*"/>
        </xsl:element>
    </xsl:template>
    <xsl:template match="*[local-name()='FormularB']//*">
        <xsl:element name="{local-name()}" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:apply-templates/>
        </xsl:element>
    </xsl:template>
    
    <!-- Formular C -->
      <xsl:template name="OC">
        <xsl:element name="OC" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:apply-templates select="*[local-name()='OwnershipRightDetails']/*[local-name()='OwnershipRightDetail'][$TaxIndex]/*[local-name()='FormularC'][$ORDIndex]/*"/>
        </xsl:element>
    </xsl:template>
    <xsl:template match="*[local-name()='FormularC']//*">
        <xsl:element name="{local-name()}" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:apply-templates/>
        </xsl:element>
    </xsl:template>
    
<!-- Company Daten-->
    <xsl:template name="getCompany">

        <xsl:variable name="HR-RC-Name" select="substring($Hrrc, 1, 30)"/>
        <xsl:variable name="ZIP" select="substring($Zip,1,6)"/>

        <xsl:element name="Company" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:attribute name="UID-BFS">
                <xsl:value-of select="$Uid"/>
            </xsl:attribute>
            <xsl:attribute name="Person">
                <xsl:value-of select="$ContactPerson"/>
            </xsl:attribute>
            <xsl:attribute name="HR-RC-Name">
                <xsl:value-of select="$HR-RC-Name"/>
            </xsl:attribute>
            <xsl:attribute name="ZIP">
                <xsl:value-of select="$ZIP"/>
            </xsl:attribute>
            <xsl:if test="$CLCompany != ''">
                <xsl:attribute name="CL">
                    <xsl:value-of select="$CLCompany"/>
                </xsl:attribute>
            </xsl:if>
            <xsl:if test="$ContactStreet != ''">
                <xsl:attribute name="Street">
                    <xsl:value-of select="$ContactStreet"/>
                </xsl:attribute>
            </xsl:if>
            <xsl:if test="$ContactPostbox != ''">
                <xsl:attribute name="Postbox">
                    <xsl:value-of select="$ContactPostbox"/>
                </xsl:attribute>                
            </xsl:if>
            <xsl:if test="$ContactLocality != ''">
                <xsl:attribute name="Locality">
                    <xsl:value-of select="$ContactLocality"/>
                </xsl:attribute>                
            </xsl:if>
            <xsl:if test="$ContactCity != ''">
                <xsl:attribute name="City">
                    <xsl:value-of select="$ContactCity"/>
                </xsl:attribute>                
            </xsl:if>
            <xsl:if test="$ContactCountry != ''">
                <xsl:attribute name="Country">
                    <xsl:value-of select="$ContactCountry"/>
                </xsl:attribute>                
            </xsl:if>
            <xsl:if test="$ContactPhone != ''">
                <xsl:attribute name="Phone">
                    <xsl:value-of select="$ContactPhone"/>
                </xsl:attribute>                
            </xsl:if>
        </xsl:element>
        
    </xsl:template>


    <xsl:template name="getPersonID">

        <xsl:element name="PersonID" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
            <xsl:attribute name="Lastname">
                <xsl:value-of select="substring(./*[local-name()='Particulars']/*[local-name()='Lastname'], 1, 30)"/>
            </xsl:attribute>
            <xsl:attribute name="Firstname">
                <xsl:value-of select="substring(./*[local-name()='Particulars']/*[local-name()='Firstname'], 1, 15)"/>
            </xsl:attribute>

            <xsl:variable name="DateOfBirth" select="./*[local-name()='Particulars']/*[local-name()='DateOfBirth']"/>

            <xsl:choose>
                <xsl:when test="count(./*[local-name()='Particulars']/*[local-name()='Social-InsuranceIdentification']/*[local-name()='SV-AS-Number']) > 0">
                    <xsl:element name="SV-AS-Nr" namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
                        <xsl:value-of
                                select="./*[local-name()='Particulars']/*[local-name()='Social-InsuranceIdentification']/*[local-name()='SV-AS-Number']"/>
                    </xsl:element>
                </xsl:when>
                <xsl:otherwise>
                    <xsl:choose>
                        <xsl:when test="string-length($DateOfBirth) > 0">
                            <xsl:element name="DateOfBirth"
                                         namespace="urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB">
                                <xsl:value-of select="$DateOfBirth"/>
                            </xsl:element>
                        </xsl:when>
                        <xsl:otherwise>
                            <unknown/>
                        </xsl:otherwise>
                    </xsl:choose>
                </xsl:otherwise>
            </xsl:choose>

            <xsl:for-each select="./*[local-name()='Particulars']/*[local-name()='Addresses']/*[local-name()='Address']">

                <!-- . == <Address> -->
                <xsl:if test="count(./*[local-name()='DepartureDate'])=0">
                    <xsl:attribute name="ZIP">
                        <xsl:value-of select="substring(./*[local-name()='ZIP-Code'], 1, 6)"/>
                    </xsl:attribute>
                    <xsl:attribute name="CL">
                        <xsl:value-of select="./*[local-name()='ComplementaryLine']"/>
                    </xsl:attribute>
                    <xsl:attribute name="Street">
                        <xsl:value-of select="./*[local-name()='Street']"/>
                    </xsl:attribute>
                    <xsl:attribute name="Postbox">
                        <xsl:value-of select="./*[local-name()='Postbox']"/>
                    </xsl:attribute>
                    <xsl:attribute name="Locality">
                        <xsl:value-of select="./*[local-name()='Locality']"/>
                    </xsl:attribute>
                    <xsl:attribute name="City">
                        <xsl:value-of select="./*[local-name()='City']"/>
                    </xsl:attribute>
                    <xsl:attribute name="Country">
                        <xsl:value-of select="./*[local-name()='Country']"/>
                    </xsl:attribute>
                </xsl:if>
            </xsl:for-each>

        </xsl:element>
    </xsl:template>

    <xsl:template name="printMessage">
        <xsl:param name="message"/>
        <xsl:if test="$verbose='true'">
            <xsl:message>
                <xsl:value-of select="$message"/>
            </xsl:message>
        </xsl:if>
    </xsl:template>

</xsl:stylesheet>