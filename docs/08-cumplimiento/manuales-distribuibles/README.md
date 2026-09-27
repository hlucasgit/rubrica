# Manuales distribuibles

Documentos `.docx` listos para entregar fuera del repositorio (a un usuario final o al equipo de un integrador externo), a diferencia del resto de `docs/`, que es documentación técnica interna en Markdown.

| Archivo | Para quién | Generado a partir de |
|---|---|---|
| `SecureSign - Manual de Instalacion y Uso.docx` | Cualquier usuario final del Firmador Local, sin conocimientos técnicos | `manual-usuario-firmador-local.md`, `compatibilidad-antivirus.md`, y el instalador MSI real (RUNBOOK 12.35) |
| `SecureSign - Manual Tecnico de Integracion.docx` | Equipo de desarrollo de un sistema externo (ERP, SGD, etc.) que integra por API | Auditado contra `src/backend/src/Gateway` y los controladores reales — mismo alcance que `../../05-integracion/manual-integracion-api.md`, en formato para entregar fuera del repositorio |

**Mantenimiento**: estos documentos se generan con la skill `docx` (biblioteca `docx` de npm) a partir de scripts que no se conservan en el repositorio. Cuando cambie algo que documentan (nueva versión del instalador, un endpoint nuevo, un campo nuevo en una respuesta), regenerar el `.docx` correspondiente en vez de editarlo a mano en Word — así se audita contra el código real igual que la primera vez, en lugar de arrastrar texto desactualizado.
